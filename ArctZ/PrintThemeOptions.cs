using System.Collections.Generic;
using System.Linq;

namespace ArctZ
{
    public static class PrintThemeOptions
    {
        private const string PrintFlag = "--theme=print";

        public static bool IsPrintMode(IEnumerable<string> args) => args.Contains(PrintFlag);
    }
}
