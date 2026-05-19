using System;
using System.Collections.Generic;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    public static class ConstantReplacer
    {
        public static string ReplaceConstants(string text, List<ConstantEntry> constants)
        {
            if (string.IsNullOrEmpty(text) || constants == null || constants.Count == 0)
                return text;

            string result = text;
            foreach (var constant in constants)
            {
                if (!string.IsNullOrEmpty(constant.Name))
                {
                    result = result.Replace($"{{{constant.Name}}}", constant.Value ?? string.Empty);
                }
            }
            return result;
        }
    }
}
