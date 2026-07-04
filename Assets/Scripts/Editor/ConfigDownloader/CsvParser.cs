using System.Collections.Generic;
using System.Text;

namespace GuildIdle.Editor.ConfigDownloader
{
    public static class CsvParser
    {
        public static bool TryParse(string csv, out List<ConfigSheetRow> rows, out string error)
        {
            rows = new List<ConfigSheetRow>();
            error = null;

            var currentRow = new List<string>();
            var currentCell = new StringBuilder();
            var inQuotes = false;

            for (var index = 0; index < csv.Length; index++)
            {
                var character = csv[index];

                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < csv.Length && csv[index + 1] == '"')
                        {
                            currentCell.Append('"');
                            index++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentCell.Append(character);
                    }

                    continue;
                }

                if (character == '"')
                {
                    if (currentCell.Length == 0)
                    {
                        inQuotes = true;
                        continue;
                    }

                    error = "Unexpected quote in unquoted CSV field.";
                    return false;
                }

                if (character == ',')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Length = 0;
                    continue;
                }

                if (character == '\n')
                {
                    AddRow(rows, currentRow, currentCell);
                    continue;
                }

                if (character == '\r')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '\n')
                        index++;

                    AddRow(rows, currentRow, currentCell);
                    continue;
                }

                currentCell.Append(character);
            }

            if (inQuotes)
            {
                error = "CSV contains an unclosed quoted field.";
                return false;
            }

            if (currentCell.Length > 0 || currentRow.Count > 0)
                AddRow(rows, currentRow, currentCell);

            return true;
        }

        private static void AddRow(List<ConfigSheetRow> rows, List<string> currentRow, StringBuilder currentCell)
        {
            currentRow.Add(currentCell.ToString());
            currentCell.Length = 0;

            var hasValue = false;
            foreach (var cell in currentRow)
            {
                if (!string.IsNullOrWhiteSpace(cell))
                {
                    hasValue = true;
                    break;
                }
            }

            if (hasValue)
                rows.Add(new ConfigSheetRow { cells = currentRow.ToArray() });

            currentRow.Clear();
        }
    }
}
