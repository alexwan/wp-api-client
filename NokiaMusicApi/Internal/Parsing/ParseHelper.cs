﻿// -----------------------------------------------------------------------
// <copyright file="ParseHelper.cs" company="Nokia">
// Copyright (c) 2013, Nokia
// All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nokia.Music.Internal.Parsing
{
    internal static class ParseHelper
    {
        /// <summary>
        /// Returns a matching enum value for specified string
        /// </summary>
        /// <typeparam name="T">The enum type</typeparam>
        /// <param name="value">The response value</param>
        /// <returns>The matching value or the default value which should be unknown or none</returns>
        internal static T ParseEnumOrDefault<T>(string value)
        {
            if (value == null)
            {
                return default(T);
            }

            try
            {
                return (T)Enum.Parse(typeof(T), value, true);
            }
            catch
            {
                return default(T);
            }
        }

        /// <summary>
        /// Loads a JObject from the json.
        /// If you use JObject.Parse it automatically changes the date to utc.
        /// This keeps the date as it is in the json, allowing us to use local dates.
        /// </summary>
        /// <param name="json">The json string.</param>
        /// <returns>The jobject</returns>
        internal static JObject ParseWithDate(string json)
        {
            JsonReader reader = new JsonTextReader(new StringReader(json));
            reader.DateParseHandling = DateParseHandling.None;
            return JObject.Load(reader);
        }
    }
}
