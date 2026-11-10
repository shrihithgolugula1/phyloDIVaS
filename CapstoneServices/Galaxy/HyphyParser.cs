using System.Text.Json;

namespace CapstoneServices.Galaxy
{
    public static class HyphyParser
    {
        public static double? ExtractOverallOmega(JsonElement root)
        {
            try
            {
                if (!root.TryGetProperty("fits", out var fits)) return null;
                if (!fits.TryGetProperty("Unconstrained model", out var unconstrained)) return null;
                if (!unconstrained.TryGetProperty("Rate Distributions", out var rateDists)) return null;
                if (!rateDists.TryGetProperty("Test", out var test)) return null;

                double weighted = 0.0;
                double totalProp = 0.0;

                foreach (var classEntry in test.EnumerateObject())
                {
                    JsonElement entry = classEntry.Value;

                    if (entry.TryGetProperty("omega", out var omegaEl) &&
                        entry.TryGetProperty("proportion", out var propEl) &&
                        omegaEl.TryGetDouble(out double omega) &&
                        propEl.TryGetDouble(out double proportion))
                    {
                        if (proportion > 0 && double.IsFinite(omega))
                        {
                            weighted += omega * proportion;
                            totalProp += proportion;
                        }
                    }
                }

                if (totalProp == 0) return null;
                return weighted / totalProp;
            }
            catch
            {
                return null;
            }
        }
    }
}
