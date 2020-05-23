using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;

namespace HexPrintFile
{
    static class Extensions
    {
        /// <summary>
        /// Extension Method: Return the DateTime for the build from a given assembly file
        /// </summary>
        /// <param name="assembly">Assembly</param>
        /// <returns>DateTime</returns>
        public static DateTime GetLinkerTimestampUtc(this Assembly assembly)
        {
            var location = assembly.Location;
            return GetLinkerTimestampUtcLocation(location);
        }

        /// <summary>
        /// Return the DateTime for the build from a given assembly file
        /// </summary>
        /// <param name="filePath">Assembly file location</param>
        /// <returns>string</returns>
        private static DateTime GetLinkerTimestampUtcLocation(string filePath)
        {
            const int peHeaderOffset = 60;
            const int linkerTimestampOffset = 8;
            var bytes = new byte[2048];

            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                file.Read(bytes, 0, bytes.Length);
            }

            var headerPos = BitConverter.ToInt32(bytes, peHeaderOffset);
            var secondsSince1970 = BitConverter.ToInt32(bytes, headerPos + linkerTimestampOffset);
            var dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return dt.AddSeconds(secondsSince1970);
        }

        /// <summary>
        /// Extension Method to get the description attribute from an Enum value
        /// </summary>
        /// <param name="value">Enum value</param>
        /// <returns>string</returns>
        public static string GetDescription(this Enum value)
        {
            Type type = value.GetType();
            string name = Enum.GetName(type, value);
            if (name != null)
            {
                FieldInfo field = type.GetField(name);
                if (field != null)
                {
                    DescriptionAttribute attr = 
                        Attribute.GetCustomAttribute(field, 
                            typeof(DescriptionAttribute)) as DescriptionAttribute;
                    if (attr != null)
                    {
                        return attr.Description;
                    }
                }
            }
            return null;
        }
    }
}