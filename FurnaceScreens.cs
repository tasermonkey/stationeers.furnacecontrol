using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Pipes;
using FurnaceControlMod.Logic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FurnaceControlMod
{
    // The board's main screen, built in code inside the source board's main screen (Screens[0]): Home, the
    // recipe list, the 10-key and the batch. Which page shows is each player's own; the recipe and amount picked
    // are the furnace's, the same on all its consoles. While a batch runs, or is done, every player sees it.
    // A slave console shows its master's furnace: these pages, or just the recipes console's page, or the 10-key.
    public class FurnaceScreens
    {
        enum Page { Home, Recipes, Amount, Batch, RecipesConsole }

        // A family of recipes to a row: the game's 18 fill five (metals take two)
        const int Columns = 5;
        const int Rows = 5;
        const int PerPage = Columns * Rows;
        const float AbortArmSeconds = 3;                                // Abort takes a second press within this
        const float SetupRow = 46;                                 // px the setup page's device list gives up to the option under it

        // Rich-text colors, on the board's dark screen
        const string Green = "#4cc47f";
        const string Red = "#f07070";
        const string Orange = "#ffa733";
        const string Grey = "#93a1ae";

        // A page of recipe buttons: the recipe list's, or the recipes console's
        class RecipeGrid
        {
            public readonly List<Button> Buttons = new List<Button>();
            public readonly List<Image> Pictures = new List<Image>();
            public Button Previous;
            public Button Next;
            public Text PageLabel;
        }

        readonly FurnaceControlBoard _board;
        readonly Font _font;
        readonly Dictionary<Page, RectTransform> _pages = new Dictionary<Page, RectTransform>();
        Page _page = Page.Home;
        bool _dirty = true;

        readonly AmountEntry _entry = new AmountEntry();
        int _recipePage;
        float _abortArmedUntil;
        ColorBlock _plainColors;

        Text _homeStatus;
        Text _homePreview;
        Text _homeReadings;
        Button _recipeButton;
        Button _amountButton;
        Button _startButton;
        Button _homeFlush;

        readonly RecipeGrid _recipes = new RecipeGrid();
        Text _recipesHeader;

        Text _amountHeader;
        Text _amountValue;
        Text _amountHint;
        readonly List<Button> _keys = new List<Button>();
        Button _amountBack;
        Button _amountConfirm;

        Text _batchHeader;
        Text _batchStatus;
        Text _batchNeeds;
        Text _batchReadings;
        Button _batchAction;
        Button _batchFlush;

        readonly RecipeGrid _consoleRecipes = new RecipeGrid();
        Image _consolePicture;
        Text _consoleInfo;
        Text _consoleStatus;

        Button _setupPictures;

        public FurnaceScreens(FurnaceControlBoard board)
        {
            _board = board;
            _font = FindFont(board);
            if (board.ButtonPrefab == null)
            {
                Debug.LogError("[FurnaceControl] the board has no ButtonPrefab: its screen stays empty");
                return;
            }
            var root = Panel(board.Screens[0].transform, "FurnaceControl");
            BuildHome(AddPage(root, Page.Home));
            BuildRecipes(AddPage(root, Page.Recipes));
            BuildAmount(AddPage(root, Page.Amount));
            BuildBatch(AddPage(root, Page.Batch));
            BuildRecipesConsole(AddPage(root, Page.RecipesConsole));
            if (board.Screens.Count > 1) BuildSetup(board.Screens[1].transform);
        }

        public void MarkDirty() => _dirty = true;

        // The furnace this console shows: its own board's, or its master's.
        FurnaceControlBoard Main => _board.Main;
        RecipeInfo Picked => Main.PickedRecipe;
        int Amount => Main.PickedAmount;

        // ---- Pages ----

        void BuildHome(RectTransform page)
        {
            Header(page, "FURNACE CONTROL");
            _homeStatus = Label(page, .05f, .73f, .95f, .88f, 24, TextAnchor.MiddleCenter);
            _recipeButton = MakeButton(page, .08f, .57f, .92f, .71f, "", () => Show(Page.Recipes), 26);
            _amountButton = MakeButton(page, .08f, .41f, .92f, .55f, "", OpenAmount, 26);
            _homePreview = Label(page, .05f, .27f, .95f, .39f, 20, TextAnchor.MiddleCenter);
            _homeReadings = Label(page, .05f, .19f, .95f, .27f, 18, TextAnchor.MiddleCenter);
            _homeFlush = MakeButton(page, .04f, .04f, .34f, .17f, "", FlushBox, 22);
            _startButton = MakeButton(page, .38f, .04f, .96f, .17f, "START", StartBatch, 28);
        }

        void BuildRecipes(RectTransform page)
        {
            _recipesHeader = Header(page, "PICK A RECIPE");
            BuildGrid(page, _recipes, .87f, .17f, PickRecipe);
            _plainColors = _recipes.Buttons[0].colors;
            MakeButton(page, .04f, .03f, .30f, .13f, "BACK", () => Show(Page.Home), 22);
        }

        // A page of recipe buttons between top and bottom, and the page turners under them.
        void BuildGrid(RectTransform page, RecipeGrid grid, float top, float bottom, Action<RecipeInfo> pick)
        {
            const float left = .04f;
            const float right = .96f;
            const float gap = .008f;
            const float width = (right - left) / Columns;
            var height = (top - bottom) / Rows;
            for (var i = 0; i < PerPage; i++)
            {
                var x = left + i % Columns * width;
                var y = top - i / Columns * height;
                var index = i;
                var button = MakeButton(page, x + gap, y - height + gap, x + width - gap, y - gap, "", () => PickAt(index, pick), 22);
                grid.Buttons.Add(button);
                grid.Pictures.Add(Picture(button.transform, .1f, .08f, .9f, .92f));
            }
            grid.Previous = MakeButton(page, .50f, .03f, .62f, .13f, "<", () => TurnPage(-1), 22);
            grid.PageLabel = Label(page, .62f, .03f, .82f, .13f, 20, TextAnchor.MiddleCenter);
            grid.Next = MakeButton(page, .82f, .03f, .94f, .13f, ">", () => TurnPage(1), 22);
        }

        void BuildAmount(RectTransform page)
        {
            _amountHeader = Header(page, "AMOUNT");
            _amountValue = Label(page, .25f, .75f, .75f, .88f, 44, TextAnchor.MiddleCenter);
            _amountHint = Label(page, .04f, .66f, .96f, .75f, 18, TextAnchor.MiddleCenter);
            string[] keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", "CLR", "0", "DEL" };
            const float left = .22f;
            const float right = .78f;
            const float top = .65f;
            const float bottom = .19f;
            const float gap = .01f;
            const float width = (right - left) / 3;
            const float height = (top - bottom) / 4;
            for (var i = 0; i < keys.Length; i++)
            {
                var x = left + i % 3 * width;
                var y = top - i / 3 * height;
                var key = keys[i];
                _keys.Add(MakeButton(page, x + gap, y - height + gap, x + width - gap, y - gap, key, () => PressKey(key), 28));
            }
            _amountBack = MakeButton(page, .04f, .03f, .30f, .15f, "BACK", () => Show(Page.Home), 24);
            _amountConfirm = MakeButton(page, .34f, .03f, .96f, .15f, "CONFIRM", Confirm, 28);
        }

        void BuildBatch(RectTransform page)
        {
            _batchHeader = Header(page, "");
            _batchStatus = Label(page, .04f, .73f, .96f, .88f, 26, TextAnchor.MiddleCenter);
            _batchNeeds = Label(page, .06f, .42f, .94f, .72f, 22, TextAnchor.UpperLeft);
            _batchReadings = Label(page, .06f, .17f, .94f, .41f, 18, TextAnchor.UpperLeft);
            _batchFlush = MakeButton(page, .04f, .03f, .34f, .15f, "", FlushBox, 22);
            _batchAction = MakeButton(page, .38f, .03f, .96f, .15f, "", BatchAction, 26);
        }

        // The recipes console: the picked recipe across the top quarter, its picture beside its range and what it
        // takes, and the recipes under it.
        void BuildRecipesConsole(RectTransform page)
        {
            _consolePicture = Picture(page, .04f, .76f, .44f, .98f);
            _consoleInfo = Label(page, .48f, .75f, .97f, .98f, 22, TextAnchor.MiddleLeft);
            BuildGrid(page, _consoleRecipes, .73f, .15f, PickOnConsole);
            _consoleStatus = Label(page, .04f, .03f, .96f, .13f, 20, TextAnchor.MiddleCenter); // over the page turners, hidden while it shows
        }

        // The setup page is the source board's: its device list gives up a row at the bottom to the board's option.
        void BuildSetup(Transform page)
        {
            var list = page.GetComponentInChildren<ScrollRect>(true);
            if (list == null) return;
            ((RectTransform)list.transform).offsetMin += new Vector2(0, SetupRow); // its top stays put
            _setupPictures = MakeButton(page, .04f, 0, .96f, 0, "", ToggleRecipePictures, 22);
            var rect = (RectTransform)_setupPictures.transform;
            rect.offsetMin = new Vector2(0, 6);
            rect.offsetMax = new Vector2(0, SetupRow - 4);
        }

        // ---- What the buttons do ----

        void Show(Page page)
        {
            _page = page;
            _dirty = true;
            Refresh();
        }

        void OpenAmount()
        {
            _entry.Set(Amount);
            Show(Page.Amount);
        }

        void PickAt(int index, Action<RecipeInfo> pick)
        {
            var cells = RecipeLayout.Cells(FurnaceRecipes.All, Columns);
            var i = _recipePage * PerPage + index;
            if (i < cells.Count && cells[i] != null) pick(cells[i]);
        }

        // The recipe list's pick goes back Home; the recipes console stays.
        void PickRecipe(RecipeInfo recipe)
        {
            _board.SendPick(recipe);
            Show(Page.Home);
        }

        void PickOnConsole(RecipeInfo recipe)
        {
            _board.SendPick(recipe);
            _dirty = true;
            Refresh();
        }

        void TurnPage(int by)
        {
            _recipePage += by;
            _dirty = true;
            Refresh();
        }

        void PressKey(string key)
        {
            if (key == "CLR") _entry.Clear();
            else if (key == "DEL") _entry.Delete();
            else _entry.Press(key[0] - '0');
            Show(Page.Amount);
        }

        // Keeps the amount; with a recipe picked, starts the batch. Either way, back to Home (the batch page
        // takes over once the batch runs); the amount console stays.
        void Confirm()
        {
            if (!_entry.IsValid)
            {
                Show(Page.Amount); // the hint says why
                return;
            }
            _board.SendAmount(_entry.Value);
            if (CanStart(_entry.Value)) _board.SendStart(Picked, _entry.Value);
            Show(Page.Home);
        }

        void StartBatch()
        {
            if (CanStart(Amount)) _board.SendStart(Picked, Amount);
            Show(Page.Home);
        }

        bool CanStart(int amount) => Picked != null && amount > 0 && Main.IsOperable && !Main.Controller.IsBusy;

        // Starts Flush Box, or cancels it while it runs.
        void FlushBox()
        {
            _board.SendFlushBox(Main.Controller.Flush == BoxFlush.None);
            _dirty = true;
            Refresh();
        }

        void ToggleRecipePictures()
        {
            _board.SendRecipePictures(!Main.RecipePictures);
            _dirty = true;
            Refresh();
        }

        // The Flush Box button's caption and whether it can be pressed. An emergency holds the box empty itself.
        void RefreshFlush(Button button)
        {
            var emergency = Main.Controller.State == BatchState.Emergency;
            var flushing = Main.Controller.Flush != BoxFlush.None && !emergency;
            Caption(button, flushing ? "CANCEL FLUSH" : "FLUSH BOX");
            button.interactable = !emergency && (flushing || Main.IsOperable);
        }

        void BatchAction()
        {
            switch (Main.Controller.State)
            {
                case BatchState.Done:
                    _board.SendAcknowledge();
                    break;
                case BatchState.Active:
                    if (Time.realtimeSinceStartup < _abortArmedUntil)
                    {
                        _abortArmedUntil = 0;
                        _board.SendAbort();
                    }
                    else
                        _abortArmedUntil = Time.realtimeSinceStartup + AbortArmSeconds;
                    break;
            }
            _dirty = true;
            Refresh();
        }

        // ---- Drawing, a few times a second while the screen shows ----

        public void Refresh()
        {
            if (_pages.Count == 0) return;
            if (!_dirty && !_board.Screens[0].activeInHierarchy) return;
            _dirty = false;
            if (_setupPictures != null)
            {
                _setupPictures.gameObject.SetActive(!_board.IsSlave); // the master's setting
                Caption(_setupPictures, Main.RecipePictures ? "Recipe list: pictures" : "Recipe list: names");
            }
            var page = ShownPage();
            foreach (var entry in _pages) entry.Value.gameObject.SetActive(entry.Key == page);
            switch (page)
            {
                case Page.Home: RefreshHome(); break;
                case Page.Recipes: RefreshRecipes(); break;
                case Page.Amount: RefreshAmount(); break;
                case Page.Batch: RefreshBatch(); break;
                case Page.RecipesConsole: RefreshRecipesConsole(); break;
            }
        }

        // A slave's job, or else the batch while there is one, or else this player's page.
        Page ShownPage()
        {
            switch (_board.Shows)
            {
                case SlaveScreen.Recipes: return Page.RecipesConsole;
                case SlaveScreen.Amount: return Page.Amount;
                default: return Main.Controller.State == BatchState.Idle ? _page : Page.Batch;
            }
        }

        void RefreshHome()
        {
            var missing = Main.MissingRoles();
            var furnace = Main.DeviceFor(Role.Furnace);
            if (missing.Count > 0)
                _homeStatus.text = Paint(Red, "Missing: " + string.Join(", ", missing.Select(Roles.Label)))
                    + "\n" + Paint(Grey, "Use a screwdriver on the console to link them.");
            else if (BoxOverPressure())
                _homeStatus.text = BoxWarning();
            else if (Main.Controller.Flush != BoxFlush.None)
                _homeStatus.text = Paint(Orange, "Flushing the box: " + FlushStage());
            else if (!furnace.OnOff)
                _homeStatus.text = Paint(Orange, "The furnace is off: turn it on");
            else
                _homeStatus.text = Paint(Green, "Ready");
            Caption(_recipeButton, Picked == null ? "Recipe: pick one" : "Recipe: " + Picked.Name);
            Caption(_amountButton, Amount > 0 ? $"Amount: {Amount}" : "Amount: type one");
            _homePreview.text = Picked == null ? "" : Preview(Picked, Amount);
            _homeReadings.text = furnace == null ? "" : Paint(Grey,
                $"Furnace {Units.Kelvin(Get(furnace, LogicType.Temperature))}, {Units.Pressure(Get(furnace, LogicType.Pressure))}    ")
                + BoxLine(withTemperature: false);
            _startButton.interactable = CanStart(Amount);
            RefreshFlush(_homeFlush);
        }

        void RefreshRecipes()
        {
            var count = RefreshGrid(_recipes, locked: false);
            _recipesHeader.text = count == 0 ? "NO RECIPES YET" : "PICK A RECIPE";
        }

        // Fills a page of recipe buttons: pictures or names, as the setup page says, and the picked one green.
        // Locked, while a batch cooks, they grey out and the batch's recipe is the one picked. Returns the
        // number of cells, 0 before the game has its recipes.
        int RefreshGrid(RecipeGrid grid, bool locked)
        {
            var cells = RecipeLayout.Cells(FurnaceRecipes.All, Columns);
            var pages = Math.Max(1, (cells.Count + PerPage - 1) / PerPage);
            _recipePage = Math.Max(0, Math.Min(pages - 1, _recipePage));
            var picked = locked ? Main.Recipe : Picked;
            for (var i = 0; i < PerPage; i++)
            {
                var index = _recipePage * PerPage + i;
                var recipe = index < cells.Count ? cells[index] : null;
                var button = grid.Buttons[i];
                button.gameObject.SetActive(recipe != null);
                if (recipe == null) continue;
                // A picture, if the setup page asks for them and the output has one; otherwise its name
                var picture = Main.RecipePictures ? FurnaceRecipes.Picture(recipe) : null;
                Caption(button, picture == null ? recipe.Name : "");
                grid.Pictures[i].sprite = picture;
                grid.Pictures[i].color = locked ? new Color(1, 1, 1, .4f) : Color.white;
                grid.Pictures[i].gameObject.SetActive(picture != null);
                var colors = _plainColors;
                if (picked != null && recipe.OutputHash == picked.OutputHash)
                {
                    colors.normalColor = Color.green; // as a linked device
                    colors.disabledColor = Color.Lerp(colors.disabledColor, Color.green, .5f);
                }
                button.colors = colors;
                button.interactable = !locked;
            }
            var paged = pages > 1 && !locked;
            grid.Previous.gameObject.SetActive(paged);
            grid.Next.gameObject.SetActive(paged);
            grid.Previous.interactable = _recipePage > 0;
            grid.Next.interactable = _recipePage < pages - 1;
            grid.PageLabel.text = paged ? $"{_recipePage + 1} / {pages}" : "";
            return cells.Count;
        }

        // The recipes console: what's picked (or cooking), and the recipes, greyed out while a batch cooks.
        void RefreshRecipesConsole()
        {
            var cooking = Main.Controller.IsBusy;
            var recipe = cooking ? Main.Recipe : Picked;
            var picture = FurnaceRecipes.Picture(recipe);
            _consolePicture.sprite = picture;
            _consolePicture.gameObject.SetActive(picture != null);
            _consoleInfo.text = recipe == null ? Paint(Grey, "Pick a recipe below") : RecipeCard(recipe);
            var count = RefreshGrid(_consoleRecipes, cooking);
            var emergency = Main.Controller.State == BatchState.Emergency;
            _consoleStatus.text = cooking ? Paint(emergency ? Red : Orange, CookingLine()) : count == 0 ? Paint(Grey, "No recipes yet") : "";
        }

        // The master's Amount page, and the amount console's: that has no BACK, and greys out while a batch cooks.
        void RefreshAmount()
        {
            var console = _board.Shows == SlaveScreen.Amount;
            var cooking = console && Main.Controller.IsBusy;
            _amountBack.gameObject.SetActive(!console);
            Place((RectTransform)_amountConfirm.transform, console ? .04f : .34f, .03f, .96f, .15f);
            foreach (var key in _keys) key.interactable = !cooking;
            _amountConfirm.interactable = !cooking;
            if (cooking && Main.Controller.State == BatchState.Emergency)
            {
                _amountHeader.text = Paint(Red, "EMERGENCY");
                _amountValue.text = "";
                _amountHint.text = Paint(Red, CookingLine());
                return;
            }
            if (cooking)
            {
                _amountHeader.text = $"COOKING {(Main.Recipe?.Name ?? "INGOTS").ToUpperInvariant()}";
                _amountValue.text = Paint(Grey, Main.Controller.Amount.ToString());
                _amountHint.text = Paint(Orange, CookingLine());
                return;
            }
            _amountHeader.text = Picked == null ? "AMOUNT" : $"{Picked.Name.ToUpperInvariant()} AMOUNT";
            _amountValue.text = _entry.Text.Length == 0 ? Paint(Grey, "0") : _entry.Text;
            if (_entry.Text.Length > 0 && !_entry.IsValid)
                _amountHint.text = Paint(Red, $"From {BatchPlan.MinAmount} to {BatchPlan.MaxAmount}");
            else if (Picked != null && _entry.IsValid)
                _amountHint.text = Preview(Picked, _entry.Value);
            else if (Picked == null && console)
                _amountHint.text = Paint(Grey, "Pick a recipe first");
            else
                _amountHint.text = Paint(Grey, $"{BatchPlan.MinAmount} to {BatchPlan.MaxAmount} ingots");
        }

        void RefreshBatch()
        {
            var controller = Main.Controller;
            var recipe = Main.Recipe;
            var name = recipe?.Name ?? "ingots";
            _batchHeader.text = $"{name.ToUpperInvariant()} x {controller.Amount}";
            var furnace = Main.DeviceFor(Role.Furnace);
            _batchReadings.text = recipe == null ? "" : Readings(recipe);
            var action = "";
            switch (controller.State)
            {
                case BatchState.Active:
                    action = Time.realtimeSinceStartup < _abortArmedUntil ? "PRESS AGAIN" : "ABORT";
                    RefreshActive(recipe, furnace, controller.Amount);
                    break;
                case BatchState.Unloading:
                    _batchStatus.text = Paint(Green, $"Done: unloading {controller.Made} {name}");
                    _batchNeeds.text = "The furnace is open until it's empty.";
                    break;
                case BatchState.Aborting:
                    _batchStatus.text = Paint(Orange, "Aborted: emptying the furnace");
                    _batchNeeds.text = $"Anything that isn't {name} comes out as slag.";
                    break;
                case BatchState.Done:
                    action = "OK";
                    _batchStatus.text = Paint(Green, $"Made {controller.Made} {name}");
                    _batchNeeds.text = "Collect them from the furnace's output, then press OK.";
                    break;
                case BatchState.Emergency:
                    _batchHeader.text = Paint(Red, "EMERGENCY");
                    _batchStatus.text = Paint(Red, "Emptying the furnace and the box");
                    _batchNeeds.text = EmergencyLines();
                    _batchReadings.text = "";
                    break;
            }
            _batchAction.gameObject.SetActive(action.Length > 0);
            Caption(_batchAction, action);
            RefreshFlush(_batchFlush);
        }

        void RefreshActive(RecipeInfo recipe, Device furnace, int amount)
        {
            var missing = Main.MissingRoles();
            if (recipe == null || furnace == null || missing.Count > 0)
            {
                _batchStatus.text = Paint(Red, "Missing: " + (recipe == null ? "the recipe" : string.Join(", ", missing.Select(Roles.Label))));
                _batchNeeds.text = "The batch waits until it's back.";
                return;
            }
            var plan = BatchPlan.For(recipe, amount, FurnaceControlBoard.Holdings(furnace));
            string message;
            if (BoxOverPressure()) message = BoxWarning();
            else if (!furnace.OnOff) message = Paint(Orange, "The furnace is off: turn it on");
            else if (plan.Unwanted.Count > 0) message = Paint(Red, "Something not in the recipe went in");
            else if (!plan.Complete) message = "Add the ore below";
            else if (InRange(recipe, furnace)) message = Paint(Green, "All in: forming the ingots");
            else message = Paint(Orange, "All in: bringing the furnace to the recipe's range");
            _batchStatus.text = $"Ready {plan.Ready} of {amount}\n{message}";

            var lines = plan.Needs.Select(need => need.ToAdd >= 0.05
                ? $"{need.Ingredient.Name}: add <b>{Units.Reagent(need.ToAdd)}</b>  " + Paint(Grey, $"({Units.Reagent(need.Have)} of {Units.Reagent(need.Want)} in)")
                : Paint(Green, $"{need.Ingredient.Name}: all {Units.Reagent(need.Want)} in")).ToList();
            if (plan.Makes > amount)
                lines.Add(Paint(Orange, $"Too much went in: the batch now makes {plan.Makes}"));
            lines.AddRange(plan.Unwanted.Select(holding =>
                Paint(Red, $"{holding.Name}: {Units.Reagent(holding.Quantity)} in, not part of {recipe.Name}. Abort to clear it.")));
            _batchNeeds.text = string.Join("\n", lines);
        }

        // What an emergency waits on: the furnace's gas and contents out, the box held empty meanwhile.
        string EmergencyLines()
        {
            var furnace = Main.DeviceFor(Role.Furnace);
            var kPa = Get(furnace, LogicType.Pressure);
            var melted = Get(furnace, LogicType.Reagents);
            var box = Get(Main.DeviceFor(Role.BoxSensor), LogicType.Pressure);
            var gas = kPa <= FurnaceController.EmergencyEmptyPressureKPa
                ? Paint(Green, "Furnace gas: vented")
                : $"Furnace gas: {Units.Pressure(kPa)}, venting";
            var contents = melted <= 0
                ? Paint(Green, "Furnace contents: out")
                : $"Furnace contents: {Units.Reagent(melted)} in, coming out";
            var held = box < 0 ? "Box: no sensor reading, emptying" : $"Box: {Units.Pressure(box)}, held empty until the furnace is";
            return string.Join("\n", gas, contents, held);
        }

        // What the batch is doing, on the recipes and amount consoles while they're greyed out.
        string CookingLine()
        {
            var controller = Main.Controller;
            var recipe = Main.Recipe;
            var name = recipe?.Name ?? "ingots";
            switch (controller.State)
            {
                case BatchState.Unloading: return $"Done: unloading {controller.Made} {name}";
                case BatchState.Aborting: return "Aborted: emptying the furnace";
                case BatchState.Emergency: return "Emergency: emptying the furnace";
            }
            var furnace = Main.DeviceFor(Role.Furnace);
            var ready = recipe == null || furnace == null ? 0 : BatchPlan.For(recipe, controller.Amount, FurnaceControlBoard.Holdings(furnace)).Ready;
            return $"Cooking {name} x {controller.Amount}: ready {ready}";
        }

        // The furnace against the recipe's range, the pumps and the box.
        string Readings(RecipeInfo recipe)
        {
            var furnace = Main.DeviceFor(Role.Furnace);
            if (furnace == null) return "";
            var kelvin = Get(furnace, LogicType.Temperature);
            var kPa = Get(furnace, LogicType.Pressure);
            return Paint(kelvin >= recipe.MinTempK && kelvin <= recipe.MaxTempK ? Green : Orange,
                    $"Furnace {Units.Kelvin(kelvin)}")
                + Paint(Grey, $"  (wants {Units.KelvinRange(recipe.MinTempK, recipe.MaxTempK)})") + "\n"
                + Paint(kPa >= recipe.MinPressureKPa && kPa <= recipe.MaxPressureKPa ? Green : Orange,
                    $"Pressure {Units.Pressure(kPa)}")
                + Paint(Grey, $"  (wants {Units.PressureRange(recipe.MinPressureKPa, recipe.MaxPressureKPa)})") + "\n"
                + $"Fuel pump {OnOff(Role.FuelPump)}    Coolant pump {OnOff(Role.CoolantPump)}\n"
                + BoxLine(withTemperature: true);
        }

        // The box's reading: red over the windows' limit.
        string BoxLine(bool withTemperature)
        {
            var box = Main.DeviceFor(Role.BoxSensor);
            var kPa = Get(box, LogicType.Pressure);
            var line = $"Box {Units.Pressure(kPa)}" + (withTemperature ? $", {Units.Kelvin(Get(box, LogicType.Temperature))}" : "");
            var flush = Main.Controller.Flush != BoxFlush.None ? Paint(Orange, " flushing: " + FlushStage()) : "";
            return Paint(FurnaceController.BoxOverPressure(kPa) ? Red : Grey, line) + flush;
        }

        string FlushStage() => Main.Controller.Flush == BoxFlush.Emptying
            ? $"emptying to {Units.Pressure(FurnaceController.FlushEmptyPressureKPa)}"
            : $"refilling to {Units.Pressure(FurnaceController.BoxTargetPressureKPa)}";

        bool BoxOverPressure() => FurnaceController.BoxOverPressure(Get(Main.DeviceFor(Role.BoxSensor), LogicType.Pressure));

        string BoxWarning() =>
            Paint(Red, $"Box over {Units.Pressure(FurnaceController.BoxWarnPressureKPa)}: Iron Windows take 200 kPa")
            + "\n" + Paint(Grey, "Check the empty vent and its exhaust pipe.");

        static bool InRange(RecipeInfo recipe, Device furnace)
        {
            var kelvin = Get(furnace, LogicType.Temperature);
            var kPa = Get(furnace, LogicType.Pressure);
            return kelvin >= recipe.MinTempK && kelvin <= recipe.MaxTempK
                && kPa >= recipe.MinPressureKPa && kPa <= recipe.MaxPressureKPa;
        }

        // What a batch of this size needs, and the recipe's range.
        static string Preview(RecipeInfo recipe, int amount)
        {
            var range = Paint(Grey, $"{Units.KelvinRange(recipe.MinTempK, recipe.MaxTempK)}, "
                + $"{Units.PressureRange(recipe.MinPressureKPa, recipe.MaxPressureKPa)}");
            if (amount <= 0) return range;
            var plan = BatchPlan.For(recipe, amount, Enumerable.Empty<Holding>());
            return "Needs " + string.Join(", ", plan.Needs.Select(need => $"{Units.Reagent(need.Want)} {need.Ingredient.Name}"))
                + "\n" + range;
        }

        // The recipes console's card: the name, the range, and what it takes in its smallest whole batch.
        static string RecipeCard(RecipeInfo recipe)
        {
            var ratio = BatchPlan.WholeRatio(recipe);
            var inputs = string.Join(" : ", recipe.Ingredients.Select((ingredient, i) => $"{ratio[i]} {ingredient.Name}"));
            return $"<b>{Paint(Orange, recipe.Name.ToUpperInvariant())}</b>\n"
                + $"{Units.KelvinRange(recipe.MinTempK, recipe.MaxTempK)}\n"
                + $"{Units.PressureRange(recipe.MinPressureKPa, recipe.MaxPressureKPa)}\n"
                + Paint(Grey, inputs);
        }

        string OnOff(Role pump) => Get(Main.DeviceFor(pump), LogicType.On) > 0 ? Paint(Green, "on") : Paint(Grey, "off");

        static double Get(Device device, LogicType type) => FurnaceControlBoard.Get(device, type);

        static string Paint(string color, string text) => $"<color={color}>{text}</color>";

        static void Caption(Button button, string caption)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null) text.text = caption;
        }

        // ---- Building the prefab (once, in PrefabFactory) ----

        // Takes the source board's controls off the main screen, keeping anything the board itself still uses.
        // Its title label goes too: it sits mid-screen, where the pages are.
        public static void EmptyMainScreen(Circuitboard board)
        {
            var main = board.Screens[0].transform;
            var keep = new Object[] { board.ScrollBar, board.DynamicGrid, board.CreateFilterButton, board.ClearFilterButton, board._btnUp, board._btnDown, board._btnSearch }
                .OfType<Component>().Select(component => component.transform).ToList();
            if (board.ButtonPrefab != null) keep.Add(board.ButtonPrefab.transform);
            for (var i = main.childCount - 1; i >= 0; i--)
            {
                var child = main.GetChild(i);
                if (keep.Any(part => part.IsChildOf(child))) continue;
                Object.DestroyImmediate(child.gameObject);
            }
        }

        // ---- Building the pages (each board, in Awake) ----

        // The game's font, from the setup page's device-button template.
        static Font FindFont(Circuitboard board)
        {
            var text = board.ButtonPrefab != null ? board.ButtonPrefab.GetComponentInChildren<Text>(true) : null;
            if (text == null || text.font == null)
                text = board.GetComponentsInChildren<Text>(true).FirstOrDefault(found => found.font != null);
            return text != null ? text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        RectTransform AddPage(RectTransform root, Page page)
        {
            var panel = Panel(root, page.ToString());
            panel.gameObject.SetActive(false);
            _pages[page] = panel;
            return panel;
        }

        static RectTransform Panel(Transform parent, string name)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            panel.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)panel.transform;
            rect.SetParent(parent, false);
            Place(rect, 0, 0, 1, 1);
            return rect;
        }

        // Anchors a rectangle by fractions of its parent, so the page fits whatever size the screen is.
        static void Place(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        Text Header(Transform parent, string caption)
        {
            var header = Label(parent, .04f, .89f, .96f, .98f, 26, TextAnchor.MiddleCenter);
            header.text = caption;
            header.fontStyle = FontStyle.Bold;
            if (ColorUtility.TryParseHtmlString(Orange, out var orange)) header.color = orange;
            return header;
        }

        Text Label(Transform parent, float xMin, float yMin, float xMax, float yMax, int size, TextAnchor alignment)
        {
            var label = new GameObject("Label", typeof(RectTransform));
            var rect = (RectTransform)label.transform;
            rect.SetParent(parent, false);
            Place(rect, xMin, yMin, xMax, yMax);
            var text = label.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        // A picture, kept to its shape, inside a button (over its caption) or on a page.
        static Image Picture(Transform parent, float xMin, float yMin, float xMax, float yMax)
        {
            var made = new GameObject("Picture", typeof(RectTransform));
            var rect = (RectTransform)made.transform;
            rect.SetParent(parent, false);
            Place(rect, xMin, yMin, xMax, yMax);
            var image = made.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false; // a click is the button's
            made.SetActive(false);
            return image;
        }

        // A button like the setup page's device buttons.
        Button MakeButton(Transform parent, float xMin, float yMin, float xMax, float yMax, string caption, UnityAction onClick, int size)
        {
            var made = Object.Instantiate(_board.ButtonPrefab, parent, false);
            made.name = "Button " + caption;
            made.SetActive(true);
            foreach (var element in made.GetComponents<LayoutElement>()) element.ignoreLayout = true;
            Place((RectTransform)made.transform, xMin, yMin, xMax, yMax);
            var button = made.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);
            button.interactable = true;
            var text = made.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = caption;
                text.alignment = TextAnchor.MiddleCenter;
                // Wrapped and truncated, or best fit can't shrink a long caption (the template overflows)
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.fontSize = size;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 10;
                text.resizeTextMaxSize = size;
                text.supportRichText = true;
            }
            return button;
        }
    }
}
