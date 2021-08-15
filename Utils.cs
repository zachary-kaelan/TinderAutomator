using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jil;

namespace TinderAutomator
{
    internal static class Utils
    {
        public static readonly DateTime UNIX_START_DATE = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        private static readonly CompareInfo COMPINF = CultureInfo.CurrentCulture.CompareInfo;
        private static readonly CompareOptions COMPOPTS = CompareOptions.IgnoreCase;
        
        /// <summary>
        /// Converts timestamp to DateTime.
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public static DateTime ConvertUnixTimestamp(long timestamp)
        {
            return timestamp > 1000000000000 ?
                UNIX_START_DATE.AddMilliseconds(timestamp).ToLocalTime() :
                UNIX_START_DATE.AddSeconds(timestamp).ToLocalTime();
        }

        /// <summary>
        /// Case-insensitive String.IndexOf
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int IIndexOf(this string str, string value) =>
            COMPINF.IndexOf(str, value, COMPOPTS);

        /// <summary>
        /// Case-insensitive String.IndexOf
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        public static int IIndexOf(this string str, string value, int startIndex) =>
            COMPINF.IndexOf(str, value, startIndex, COMPOPTS);

        /// <summary>
        /// Case-insensitive String.Contains
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool IContains(this string str, string value) =>
            str.IIndexOf(value) >= 0;

        /// <summary>
        /// Case-insensitive number of occurrences of a string within another string.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int ICount(this string str, string value)
        {
            int count = 0;
            int index = str.IIndexOf(value);
            while (index >= 0)
            {
                ++count;
                index = str.IIndexOf(value, index + value.Length);
            }
            return count;
        }
        public static void SaveDictAs<K, V>(this IEnumerable<KeyValuePair<K, V>> dict, string path, string separator = " :=: ")
        {
            if (dict != null)
                File.WriteAllLines(
                    path, dict.Select(
                        kv => kv.Key.ToString() + separator + kv.Value.ToString()
                    )
                );
        }

        #region SaveAs
        public static void SaveAs(this Object obj, string path)
        {
            obj.SaveAs(path, Options.ExcludeNullsIncludeInherited);
        }

        public static void SaveAs(this Object obj, string path, Options opts)
        {
            if (obj != null)
                File.WriteAllText(path, JSON.Serialize(obj, opts));
        }

        public static void SaveAs(this Object obj, string path, Encoding encoding)
        {
            obj.SaveAs(path, Options.ExcludeNullsIncludeInherited, encoding);
        }

        public static void SaveAs(this Object obj, string path, Options opts, Encoding encoding)
        {
            if (obj != null)
                File.WriteAllText(path, JSON.Serialize(obj, opts), encoding);
        }
        #endregion

        #region LoadJSON
        public static T LoadJSON<T>(string path, Options opts)
        {
            return JSON.Deserialize<T>(File.ReadAllText(path), opts);
        }

        public static T LoadJSON<T>(string path)
        {
            return LoadJSON<T>(path, Options.ExcludeNullsIncludeInherited);
        }

        public static T LoadJSON<T>(string path, Options opts, Encoding encoding)
        {
            return JSON.Deserialize<T>(File.ReadAllText(path, encoding), opts);
        }

        public static T LoadJSON<T>(string path, Encoding encoding)
        {
            return LoadJSON<T>(path, Options.ExcludeNullsIncludeInherited, encoding);
        }
        #endregion
    }
}
