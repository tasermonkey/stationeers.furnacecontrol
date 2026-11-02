using System.Xml.Serialization;
using Assets.Scripts.Objects.Items;
using FurnaceControlMod.Logic;

namespace FurnaceControlMod
{
    // The board's own state, beside the linked devices the game saves for every circuitboard.
    [XmlInclude(typeof(CircuitboardSaveData))]
    public class FurnaceControlSaveData : CircuitboardSaveData
    {
        [XmlArray("Roles"), XmlArrayItem("RoleDevice")] public RoleDevice[] Roles;

        [XmlElement] public int State;
        [XmlElement] public int RecipeHash;
        [XmlElement] public int Amount;
        [XmlElement] public int Made;
        [XmlElement] public int Flush;
        [XmlElement] public bool RecipePictures; // Option to show/hide Recipes in selection screen
        [XmlElement] public int PickedRecipe;
        [XmlElement] public int PickedAmount;

        [XmlArray("SlaveScreens"), XmlArrayItem("SlaveConsole")] public SlaveConsole[] SlaveScreens;
    }

    public class RoleDevice
    {
        [XmlAttribute] public long ReferenceId;
        [XmlAttribute] public Role Role;
    }

    public class SlaveConsole
    {
        [XmlAttribute] public long ReferenceId;
        [XmlAttribute] public SlaveScreen Screen;
    }
}
