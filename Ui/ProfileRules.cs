namespace TunerFest.Ui;

/// <summary>Validation that used to live in MainForm.SaveSelection; the shell runs it before every run or PNG check.</summary>
public static class ProfileRules
{
    public static void Validate(Profile p)
    {
        if (p.FirstOptional && p.SecondOptional)
            throw new InvalidOperationException(L.T("Принаймні один перк має бути обов\u0027язковим."));
        if (!p.AutoSet)
        {
            string[] names = [p.First, p.FirstAlternative, p.Second, p.SecondAlternative];
            var used = names.Where(n => n != "").ToArray();
            if (used.Distinct().Count() != used.Length)
                throw new InvalidOperationException(L.T("Перки та альтернативи мають бути різними."));
        }
        else if (p.AutoSecondManual)
        {
            if (p.Second == "")
                throw new InvalidOperationException(L.T("Оберіть другий перк або вимкніть «Другий перк вручну»."));
            if (p.Second == p.SecondAlternative)
                throw new InvalidOperationException(L.T("Другий перк і його альтернатива мають бути різними."));
        }
        PressTiming.Validate(p);
        if (!p.AutoSet && (p.First == "" || p.Second == "" || p.First == p.Second))
            throw new InvalidOperationException(L.T("Оберіть два різні перки."));
        if (!p.AutoSet && !p.IgnoreSet && p.Set == "")
            throw new InvalidOperationException(L.T("Оберіть сет або увімкніть «Не зважати на сет»."));
    }
}
