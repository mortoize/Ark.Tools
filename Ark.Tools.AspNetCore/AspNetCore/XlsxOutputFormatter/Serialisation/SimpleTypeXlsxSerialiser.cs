﻿using System;
using System.Collections;
using util = Ark.AspNetCore.XlsxOutputFormatter.FormatterUtils;

namespace Ark.AspNetCore.XlsxOutputFormatter.Serialisation
{
    /// <summary>
    /// Custom serialiser for primitives and other simple types.
    /// </summary>
    public class SimpleTypeXlsxSerialiser : IXlsxSerialiser
    {
        public bool IgnoreFormatting
        {
            get { return true; }
        }
        
        public bool CanSerialiseType(Type valueType, Type itemType)
        {
            itemType = Nullable.GetUnderlyingType(itemType) ?? itemType;

            return util.IsSimpleType(itemType);
        }

        public void Serialise(Type itemType, object value, XlsxDocumentBuilder document)
        {
            // Can't convert IEnumerable<primitive> to IEnumerable<object>
            var values = (IEnumerable)value;

            foreach (var val in values)
            {
                document.AppendRow(new object[] { val });
            }
        }
    }
}
