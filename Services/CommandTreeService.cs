using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    public static class CommandTreeService
    {
        public static CommandEntry? FindByHotkey(List<CommandEntry> commands, string hotkey, HotkeyType type)
        {
            foreach (var command in commands)
            {
                if (command.Hotkey == hotkey && command.HotkeyType == type) return command;
                if (command.Children != null)
                {
                    var found = FindByHotkey(command.Children, hotkey, type);
                    if (found != null) return found;
                }
            }
            return null;
        }

        public static bool TryFindPath(List<CommandEntry> list, CommandEntry target, List<CommandEntry> path)
        {
            foreach (var item in list)
            {
                if (item == target) return true;
                if (item.Children != null)
                {
                    path.Add(item);
                    if (TryFindPath(item.Children, target, path)) return true;
                    path.RemoveAt(path.Count - 1);
                }
            }
            return false;
        }

        public static bool Replace(List<CommandEntry> list, CommandEntry target, CommandEntry newCommand)
        {
            int index = list.IndexOf(target);
            if (index >= 0)
            {
                list[index] = newCommand;
                return true;
            }
            foreach (var item in list)
            {
                if (item.Children != null && Replace(item.Children, target, newCommand)) return true;
            }
            return false;
        }

        public static bool Remove(List<CommandEntry> list, CommandEntry target)
        {
            if (list.Remove(target)) return true;
            foreach (var item in list)
            {
                if (item.Children != null && Remove(item.Children, target)) return true;
            }
            return false;
        }

    }
}
