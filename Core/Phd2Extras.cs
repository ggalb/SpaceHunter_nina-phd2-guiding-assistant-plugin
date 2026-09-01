using NINA.Equipment.Equipment.MyGuider.PHD2;
using System;
using System.Threading.Tasks;

namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    // N.I.N.A.'s own PHD2Methods.cs does not define request/response pairs for get_algo_param(_names) -
    // Phd2GetAlgoParam there carries no parameters at all. Both are needed to read PHD2's current
    // min-move value before and after applying the Guiding Assistant's recommendations, so they are
    // completed here rather than in N.I.N.A. core.

    internal sealed class Phd2GetAlgoParamNamesCall : Phd2Method<string[]> {
        public override string Method => "get_algo_param_names";
    }

    internal sealed class Phd2GetAlgoParamCall : Phd2Method<object[]> {
        public override string Method => "get_algo_param";
    }

    internal sealed class StringArrayPhdMethodResponse : PhdMethodResponse {
        public string[] result;
    }

    internal sealed class DoublePhdMethodResponse : PhdMethodResponse {
        public double result;
    }

    /// <summary>Convenience wrappers around <see cref="PHD2Guider.SendMessage{T}"/> for the calls this plugin needs beyond what N.I.N.A. core already covers.</summary>
    internal static class Phd2Extras {

        public static async Task<bool> GetCalibrated(PHD2Guider guider) {
            var response = await guider.SendMessage<BooleanPhdMethodResponse>(new Phd2GetCalibrated());
            ThrowOnError(response, "get_calibrated");
            return response.result;
        }

        /// <summary>Finds the guide algorithm's min-move parameter name on an axis. Different algorithms spell it slightly differently, so this matches loosely, exactly like the name lookup PHD2's own docs describe.</summary>
        public static async Task<string> GetMinMoveParamName(PHD2Guider guider, string axis) {
            try {
                var response = await guider.SendMessage<StringArrayPhdMethodResponse>(new Phd2GetAlgoParamNamesCall { Parameters = new[] { axis } });
                if (response?.result != null) {
                    foreach (var name in response.result) {
                        var lettersOnly = new string(Array.FindAll(name.ToCharArray(), char.IsLetter));
                        if (lettersOnly.IndexOf("minmove", StringComparison.OrdinalIgnoreCase) >= 0) return name;
                    }
                }
            } catch { /* fall through to the default below */ }
            return "MinMove";
        }

        public static async Task<double?> GetMinMove(PHD2Guider guider, string axis, string paramName) {
            try {
                var response = await guider.SendMessage<DoublePhdMethodResponse>(new Phd2GetAlgoParamCall { Parameters = new object[] { axis, paramName } });
                if (response?.error != null) return null;
                return response.result;
            } catch {
                return null;
            }
        }

        private static void ThrowOnError(PhdMethodResponse response, string method) {
            if (response?.error != null) {
                throw new InvalidOperationException($"PHD2 '{method}' returned error: {response.error.message}");
            }
        }
    }
}
