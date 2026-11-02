namespace FurnaceControlMod.Logic
{
    // The amount being typed on the 10-key. Up to three digits, so 999 can be typed but only 1-500 confirmed.
    public class AmountEntry
    {
        public const int MaxDigits = 3;

        string _digits = "";

        public string Text => _digits;
        public int Value => _digits.Length == 0 ? 0 : int.Parse(_digits);
        public bool IsValid => Value >= BatchPlan.MinAmount && Value <= BatchPlan.MaxAmount;

        public void Press(int digit)
        {
            if (digit < 0 || digit > 9 || _digits.Length >= MaxDigits) return;
            if (digit == 0 && _digits.Length == 0) return; // no leading zeros
            _digits += digit;
        }

        public void Delete()
        {
            if (_digits.Length > 0) _digits = _digits.Substring(0, _digits.Length - 1);
        }

        public void Clear() => _digits = "";

        // Starts from an amount already chosen, or empty for none.
        public void Set(int amount) => _digits = amount > 0 ? amount.ToString() : "";
    }
}
