using FastChartsScene.Charts;
using FastChartsScene.Core;
using FastChartsScene.Examples;
using FastChartsScene.SignalProcessing;

namespace FastChartsScene.Tests;
internal static partial class Program
{
    static partial void RegisterSpectrogramReviewTests()
    {
        Add("STFT rows are frequency-major calibrated dB matching individual spectra", () => {
            const double rate = 1024;
            var samples = Enumerable.Range(0, 1024).Select(i => Math.Sin(2*Math.PI*64*i/rate) + .2*Math.Cos(2*Math.PI*180*i/rate)).ToArray();
            var result = Fourier.Spectrogram(samples, rate, 256, 64);
            for (var col = 0; col < result.TimeBins; col++)
            {
                var spectrum = Fourier.Spectrum(samples.AsSpan(col * 64, 256), rate);
                for (var row = 0; row < result.FrequencyBins; row++)
                    Equal(20 * Math.Log10(Math.Max(spectrum.Amplitudes[row], 1e-12)), result.Values[row * result.TimeBins + col]);
                var peak = Enumerable.Range(0, result.FrequencyBins).MaxBy(row => result.Values[row * result.TimeBins + col]);
                Equal(64, peak * result.FrequencyStep);
                True(Math.Abs(result.Values[peak * result.TimeBins + col]) < .01);
            }
            Equal(64 / rate, result.TimeStep); Equal(4, result.FrequencyStep);
        });
        Add("STFT scratch allocation does not scale with the number of windows", () => {
            var samples = new double[8192]; Fourier.Spectrogram(samples, 1024);
            var before = GC.GetAllocatedBytesForCurrentThread(); var result = Fourier.Spectrogram(samples, 1024);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            True(allocated < result.Values.Length * 8L + 256 * 24 + 2048, $"STFT allocated {allocated} bytes.");
            True(result.Values.All(value => value == -240));
        });
        Add("STFT validates rates, entire input and window enums", () => {
            Throws<ArgumentOutOfRangeException>(() => Fourier.Spectrogram(new double[256], 0));
            Throws<ArgumentOutOfRangeException>(() => Fourier.Spectrogram(new double[256], double.NaN));
            var samples = new double[257]; samples[^1] = double.NaN;
            Throws<ArgumentException>(() => Fourier.Spectrogram(samples, 1024));
            Throws<ArgumentOutOfRangeException>(() => Fourier.Window((WindowFunction)100, 0, 1));
            Throws<ArgumentOutOfRangeException>(() => Fourier.Spectrum(new double[8], 100, (WindowFunction)100));
        });
        Add("spectrogram example preserves STFT values and its expected chirp ridge", () => {
            const double rate = 1024;
            var samples = Enumerable.Range(0, 8192).Select(i => { var t = i / rate; return Math.Sin(2*Math.PI*(25*t+14*t*t))+.3*Math.Sin(2*Math.PI*175*t); }).ToArray();
            var stft = Fourier.Spectrogram(samples, rate, 256, 64);
            using var d = ExampleCatalog.Find("spectrogram").Create(); var heat = (HeatmapSeries)d.Series[0];
            True(heat.Grid.Values.Span.SequenceEqual(stft.Values));
            True(heat.ColorScale.Range == new AxisRange(-100, 0));
            for (var col = 0; col < stft.TimeBins; col += 17)
            {
                var peak = Enumerable.Range(0, stft.FrequencyBins).MaxBy(row => heat.Grid[col, row]);
                var expected = 25 + 28 * (col * stft.TimeStep + 255 / (2 * rate));
                True(Math.Abs(heat.Grid.Y(peak) - expected) <= 8, $"Chirp ridge at column {col} is {heat.Grid.Y(peak)} Hz, expected near {expected} Hz.");
                True(heat.Grid[col, peak] > -6);
            }
        });
    }
}
