using System;

namespace WinSender.Abr;

public enum QualityLevel
{
    High,   // 1080p30, 30 Mbps
    Medium, // 720p30,  10 Mbps
    Low     // 480p30,   4 Mbps
}

public record QualityProfile(int Width, int Height, int Fps, int TargetBitrateKbps);

public class AbrController
{
    private static readonly QualityProfile[] Profiles = {
        new(1920, 1080, 30, 30000),
        new(1280, 720,  30, 15000),
        new(854,  480,  30, 10000)
    };

    private QualityLevel _current = QualityLevel.High;
    private int _stableSeconds = 0;
    private int _simulateBwKbps = 0;
    private DateTime _lastChange = DateTime.UtcNow;

    public event EventHandler<QualityProfile>? QualityChanged;

    public void SetSimulateBandwidth(int kbps)
    {
        _simulateBwKbps = kbps;
        Console.WriteLine($"[ABR] Bandwidth simulation: {kbps} Kbps");
        EvaluateQuality(kbps);
    }

    public void OnRtcpFeedback(double lossRate, double rttMs)
    {
        if (_simulateBwKbps > 0) return;

        var effectiveBw = EstimateBandwidth(lossRate, rttMs);
        EvaluateQuality(effectiveBw);
    }

    private void EvaluateQuality(int availableBwKbps)
    {
        var target = availableBwKbps switch
        {
            > 4000 => QualityLevel.High,
            > 2000 => QualityLevel.Medium,
            _      => QualityLevel.Low
        };

        if (target < _current)
        {
            ChangeQuality(target);
        }
        else if (target > _current)
        {
            _stableSeconds++;
            if (_stableSeconds >= 10)
            {
                ChangeQuality(target);
                _stableSeconds = 0;
            }
        }
        else
        {
            _stableSeconds++;
        }
    }

    private void ChangeQuality(QualityLevel level)
    {
        if (_current == level) return;
        _current = level;
        _lastChange = DateTime.UtcNow;
        var profile = Profiles[(int)level];
        Console.WriteLine($"[ABR] Quality changed to {level}: {profile.Width}x{profile.Height}@{profile.Fps}fps {profile.TargetBitrateKbps}kbps");
        QualityChanged?.Invoke(this, profile);
    }

    private static int EstimateBandwidth(double lossRate, double rttMs)
    {
        var baseBw = 8000;
        var lossPenalty = (int)(baseBw * lossRate * 2);
        var rttPenalty = rttMs > 100 ? (int)((rttMs - 100) * 20) : 0;
        return Math.Max(500, baseBw - lossPenalty - rttPenalty);
    }

    public QualityProfile GetCurrentProfile() => Profiles[(int)_current];
}
