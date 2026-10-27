namespace TunerFest;
public static class PressTiming
{
    public static void Validate(Profile profile)
    {
        if(profile.ExitPauseMs<0||profile.ExitPauseMs>5000)throw new InvalidOperationException("Додаткова пауза Esc / B має бути від 0 до 5000 мс.");
        if(profile.RandomPause)
        {
            if(profile.PauseMinMs<50||profile.PauseMaxMs>5000||profile.PauseMinMs>profile.PauseMaxMs)
                throw new InvalidOperationException("Пауза: мінімум від 50 мс, максимум до 5000 мс; мінімум не може перевищувати максимум.");
        }
        else if(profile.Delay<50||profile.Delay>5000)throw new InvalidOperationException("Фіксована пауза має бути від 50 до 5000 мс.");
    }
    public static int NextDelay(Profile profile,Random? random=null)
    {
        Validate(profile);
        return profile.RandomPause?(random??Random.Shared).Next(profile.PauseMinMs,profile.PauseMaxMs+1):profile.Delay;
    }
}
