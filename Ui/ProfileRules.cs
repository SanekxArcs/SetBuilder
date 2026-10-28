namespace TunerFest.Ui;

/// <summary>Validation that used to live in MainForm.SaveSelection; the shell runs it before every run or PNG check.</summary>
public static class ProfileRules
{
    public static void Validate(Profile p)
    {
        if (p.FirstOptional && p.SecondOptional)
            throw new InvalidOperationException("Принаймні один перк має бути обов\u0027язковим.");
        if (!p.AutoSet)
        {
            string[] names = [p.First, p.FirstAlternative, p.Second, p.SecondAlternative];
            var used = names.Where(n => n != "").ToArray();
            if (used.Distinct().Count() != used.Length)
                throw new InvalidOperationException("Перки та альтернативи мають бути різними.");
        }
        PressTiming.Validate(p);
        if (!p.AutoSet && (p.Set == "" || p.First == "" || p.Second == "" || p.First == p.Second))
            throw new InvalidOperationException("Оберіть сет і два різні перки.");
    }
}
