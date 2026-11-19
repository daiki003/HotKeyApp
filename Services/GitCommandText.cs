namespace HotKeyCommandApp.Services
{
    public static class GitCommandText
    {
        public static List<string> ParseArguments(string commandLine)
        {
            var args = new List<string>();
            bool inQuotes = false;
            string currentArg = string.Empty;
            foreach (char c in commandLine)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(currentArg))
                    {
                        args.Add(currentArg);
                        currentArg = string.Empty;
                    }
                }
                else
                {
                    currentArg += c;
                }
            }
            if (!string.IsNullOrEmpty(currentArg)) args.Add(currentArg);
            return args;
        }

        public static int GetRequiredArgumentCount(IEnumerable<string> templates)
        {
            int requiredCount = 0;
            foreach (string template in templates)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (template.Contains($"{{{i}}}")) requiredCount = Math.Max(requiredCount, i + 1);
                }
            }
            return requiredCount;
        }

        public static string ReplaceArguments(string template, IReadOnlyList<string> arguments)
        {
            for (int i = 0; i < 10; i++)
                template = template.Replace($"{{{i}}}", i < arguments.Count ? arguments[i] : string.Empty);
            return template;
        }
    }
}
