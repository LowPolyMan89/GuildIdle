using System.Net;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GuildIdle.Editor
{
    public static class LocalisationAutoTranslator
    {
        private const string SourceLanguage = "ru";
        private const string NewLinePlaceholder = " ||| ";

        public static bool TryTranslate(string text, string targetLanguage, out string translatedText, out string error)
        {
            translatedText = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Russian source text is empty.";
                return false;
            }

            var preparedText = text.Replace("\n", NewLinePlaceholder);
            var url = string.Format(
                "https://translate.google.com/translate_a/single?client=gtx&dt=t&sl={0}&tl={1}&q={2}",
                SourceLanguage,
                targetLanguage,
                WebUtility.UrlEncode(preparedText));

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 20;
                request.SendWebRequest();

                while (!request.isDone)
                {
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    error = $"Translate request error ({targetLanguage}): {request.error}";
                    Debug.LogError(error);
                    return false;
                }

                if (!TryParseGoogleResponse(request.downloadHandler.text, out translatedText))
                {
                    error = $"Translate parse error ({targetLanguage}).";
                    Debug.LogError($"{error} Server response: {request.downloadHandler.text}");
                    return false;
                }

                translatedText = translatedText.Replace(NewLinePlaceholder, "\n");
                return true;
            }
        }

        private static bool TryParseGoogleResponse(string response, out string translatedText)
        {
            translatedText = null;

            if (string.IsNullOrEmpty(response))
                return false;

            var result = new StringBuilder();
            var depth = 0;
            var segmentStringIndex = 0;
            var inString = false;

            for (var i = 0; i < response.Length; i++)
            {
                var c = response[i];

                if (inString)
                    continue;

                if (c == '[')
                {
                    depth++;
                    if (depth == 3)
                        segmentStringIndex = 0;

                    continue;
                }

                if (c == ']')
                {
                    if (depth == 2 && result.Length > 0)
                    {
                        translatedText = result.ToString();
                        return true;
                    }

                    depth--;
                    continue;
                }

                if (c == '"' && depth == 3)
                {
                    inString = true;
                    var value = ReadJsonString(response, ref i);
                    inString = false;

                    if (segmentStringIndex == 0)
                        result.Append(value);

                    segmentStringIndex++;
                }
            }

            translatedText = result.Length > 0 ? result.ToString() : null;
            return translatedText != null;
        }

        private static string ReadJsonString(string json, ref int index)
        {
            var builder = new StringBuilder();

            for (index++; index < json.Length; index++)
            {
                var c = json[index];
                if (c == '"')
                    return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                index++;
                if (index >= json.Length)
                    break;

                AppendEscapedCharacter(json, ref index, builder);
            }

            return builder.ToString();
        }

        private static void AppendEscapedCharacter(string json, ref int index, StringBuilder builder)
        {
            var escaped = json[index];
            if (escaped == '"' || escaped == '\\' || escaped == '/')
                builder.Append(escaped);
            else if (escaped == 'b')
                builder.Append('\b');
            else if (escaped == 'f')
                builder.Append('\f');
            else if (escaped == 'n')
                builder.Append('\n');
            else if (escaped == 'r')
                builder.Append('\r');
            else if (escaped == 't')
                builder.Append('\t');
            else if (escaped == 'u' && index + 4 < json.Length)
            {
                var hex = json.Substring(index + 1, 4);
                if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                    builder.Append((char)code);

                index += 4;
            }
        }
    }
}
