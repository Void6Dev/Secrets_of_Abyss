using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria;

namespace SoA.Common.UI
{
    public enum DevStatus
    {
        Todo,
        Doing,
        Done,
        Later
    }

    public sealed class DevBoardTask
    {
        public string Id;
        public string Title;
        public readonly List<string> Details = new();
        public DevStatus Status;
    }

    public sealed class DevBoardSection
    {
        public string Name;
        public bool Collapsed;
        public readonly List<DevBoardTask> Tasks = new();

        public int DoneCount
        {
            get
            {
                int done = 0;
                foreach (DevBoardTask task in Tasks)
                {
                    if (task.Status == DevStatus.Done)
                        done++;
                }
                return done;
            }
        }
    }

    // Доска задач лежит в файле рядом с сейвами, а не в коде: правка файла видна
    // в игре сразу, без пересборки мода и перезахода. Формат намеренно текстовый —
    // его читают и правят и человек, и Claude, и git видит понятный диff.
    //
    //   // строка комментария
    //   !hide-done            скрывать выполненные (состояние вида)
    //   # Секция              заголовок; #- вместо # — секция свёрнута
    //   [x] id | Заголовок    задача; статусы: [ ] новое, [>] в работе, [x] готово, [~] отложено
    //       любой текст       строка с отступом — подробность предыдущей задачи
    public static class DevBoard
    {
        public const string FileName = "SoADevBoard.txt";
        public const string ShotFolderName = "SoADevShots";
        public const string NotesSectionName = "Заметки из игры";

        private const string LegacyFileName = "SoADevTasks.txt";
        private const string HideDoneMarker = "!hide-done";

        public static readonly List<DevBoardSection> Sections = new();

        public static bool HideDone;

        // Текст последней ошибки чтения или записи. Показывается прямо в меню:
        // молча проглоченная ошибка в дев-инструменте хуже, чем некрасивая строка
        public static string Error { get; private set; }

        public static string FilePath => Path.Combine(Main.SavePath, FileName);

        public static string ShotFolderPath => Path.Combine(Main.SavePath, ShotFolderName);

        public static int TotalCount
        {
            get
            {
                int total = 0;
                foreach (DevBoardSection section in Sections)
                    total += section.Tasks.Count;
                return total;
            }
        }

        public static int DoneTotalCount
        {
            get
            {
                int done = 0;
                foreach (DevBoardSection section in Sections)
                    done += section.DoneCount;
                return done;
            }
        }

        public static void Reload()
        {
            Sections.Clear();
            Error = null;
            HideDone = false;

            try
            {
                if (!File.Exists(FilePath))
                {
                    Error = "файла доски нет: " + FilePath;
                    return;
                }

                DevBoardSection section = null;
                DevBoardTask task = null;

                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("//"))
                        continue;

                    if (line == HideDoneMarker)
                    {
                        HideDone = true;
                        continue;
                    }

                    if (line.StartsWith("#"))
                    {
                        section = new DevBoardSection
                        {
                            Name = line.TrimStart('#', '-', ' '),
                            Collapsed = line.StartsWith("#-")
                        };
                        Sections.Add(section);
                        task = null;
                        continue;
                    }

                    if (TryParseTask(line, out DevBoardTask parsed))
                    {
                        section ??= AddSection("Разное");
                        section.Tasks.Add(parsed);
                        task = parsed;
                        continue;
                    }

                    // Строка с отступом — подробность задачи. Отступ уже съеден Trim,
                    // поэтому опознаём по тому, что задача открыта, а формат строки не задачный
                    task?.Details.Add(line);
                }

                MigrateLegacyState();
            }
            catch (Exception exception)
            {
                Error = "доска не прочитана: " + exception.Message;
            }
        }

        private static DevBoardSection AddSection(string name)
        {
            var section = new DevBoardSection { Name = name };
            Sections.Add(section);
            return section;
        }

        private static bool TryParseTask(string line, out DevBoardTask task)
        {
            task = null;
            if (line.Length < 4 || line[0] != '[' || line[2] != ']')
                return false;

            string rest = line[3..].Trim();
            int separator = rest.IndexOf('|');
            string id = separator > 0 ? rest[..separator].Trim() : rest;
            string title = separator > 0 ? rest[(separator + 1)..].Trim() : rest;
            if (id.Length == 0)
                return false;

            task = new DevBoardTask
            {
                Id = id,
                Title = title.Length > 0 ? title : id,
                Status = FromChar(line[1])
            };
            return true;
        }

        // Старый файл хранил только Id выполненных пунктов. Переносим отметки один раз
        // и удаляем его, чтобы дальше источник правды был один
        private static void MigrateLegacyState()
        {
            string legacyPath = Path.Combine(Main.SavePath, LegacyFileName);
            if (!File.Exists(legacyPath))
                return;

            var done = new HashSet<string>(File.ReadAllLines(legacyPath));
            foreach (DevBoardSection section in Sections)
            {
                foreach (DevBoardTask task in section.Tasks)
                {
                    if (done.Contains(task.Id))
                        task.Status = DevStatus.Done;
                }
            }

            File.Delete(legacyPath);
            Save();
        }

        // Перед записью файл перечитывается: пока игра открыта, доску мог поправить
        // Claude, и затирать его правки статусами из памяти нельзя
        public static void Save()
        {
            var statuses = new Dictionary<string, DevStatus>();
            var collapsed = new HashSet<string>();
            bool hideDone = HideDone;

            foreach (DevBoardSection section in Sections)
            {
                if (section.Collapsed)
                    collapsed.Add(section.Name);
                foreach (DevBoardTask task in section.Tasks)
                    statuses[task.Id] = task.Status;
            }

            if (File.Exists(FilePath))
            {
                Reload();
                foreach (DevBoardSection section in Sections)
                {
                    section.Collapsed = collapsed.Contains(section.Name);
                    foreach (DevBoardTask task in section.Tasks)
                    {
                        if (statuses.TryGetValue(task.Id, out DevStatus status))
                            task.Status = status;
                    }
                }
            }

            HideDone = hideDone;
            Write();
        }

        private static void Write()
        {
            try
            {
                var text = new StringBuilder();
                text.AppendLine("// Доска задач SoA. Правится и из игры, и снаружи — игра перечитывает файл при открытии меню (F5 — вручную).");
                text.AppendLine("// [ ] новое   [>] в работе   [x] готово   [~] отложено");
                text.AppendLine("// # Секция, #- свёрнутая секция, строка с отступом — подробность задачи.");
                text.AppendLine();

                if (HideDone)
                {
                    text.AppendLine(HideDoneMarker);
                    text.AppendLine();
                }

                foreach (DevBoardSection section in Sections)
                {
                    text.AppendLine((section.Collapsed ? "#- " : "# ") + section.Name);
                    foreach (DevBoardTask task in section.Tasks)
                    {
                        text.AppendLine($"[{ToChar(task.Status)}] {task.Id} | {task.Title}");
                        foreach (string detail in task.Details)
                            text.AppendLine("    " + detail);
                    }
                    text.AppendLine();
                }

                File.WriteAllText(FilePath, text.ToString());
            }
            catch (Exception exception)
            {
                Error = "доска не записана: " + exception.Message;
            }
        }

        // Заметка из игры: попадает в свою секцию и живёт в том же файле,
        // поэтому доходит до Claude без пересказа в чате
        public static void AddNote(string title, params string[] details)
        {
            DevBoardSection notes = Sections.Find(section => section.Name == NotesSectionName);
            if (notes == null)
            {
                notes = new DevBoardSection { Name = NotesSectionName };
                Sections.Insert(0, notes);
            }

            var task = new DevBoardTask
            {
                Id = "note_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Title = title
            };
            task.Details.AddRange(details);
            notes.Tasks.Insert(0, task);

            Save();
        }

        public static void Cycle(DevBoardTask task, int direction)
        {
            const int Count = 4;
            int next = ((int)task.Status + direction % Count + Count) % Count;
            task.Status = (DevStatus)next;
            Save();
        }

        private static DevStatus FromChar(char mark) => mark switch
        {
            'x' or 'X' => DevStatus.Done,
            '>' => DevStatus.Doing,
            '~' => DevStatus.Later,
            _ => DevStatus.Todo
        };

        private static char ToChar(DevStatus status) => status switch
        {
            DevStatus.Done => 'x',
            DevStatus.Doing => '>',
            DevStatus.Later => '~',
            _ => ' '
        };
    }
}
