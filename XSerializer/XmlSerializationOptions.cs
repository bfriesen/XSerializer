﻿using System;
using System.Text;
using System.Xml.Serialization;

namespace XSerializer
{
    public class XmlSerializationOptions : IXmlSerializerOptions, ISerializeOptions
    {
        internal XmlSerializationOptions()
        {
            Encoding = Encoding.UTF8;
            Namespaces = new XmlSerializerNamespaces();
            ShouldRedact = true;
        }

        public Encoding Encoding { get; private set; }
        public string DefaultNamespace { get; private set; }
        public XmlSerializerNamespaces Namespaces { get; private set; }
        public bool ShouldIndent { get; private set; }
        public string RootElementName { get; private set; }
        public bool ShouldAlwaysEmitTypes { get; private set; }
        public bool ShouldRedact { get; private set; }
        public Type[] ExtraTypes { get; internal set; }
        public RedactAttribute RedactAttribute { get { return null; } }
        public bool TreatEmptyElementAsString { get; private set; }

        public XmlSerializationOptions WithEncoding(Encoding encoding)
        {
            Encoding = encoding;
            return this;
        }

        public XmlSerializationOptions WithDefaultNamespace(string defaultNamespace)
        {
            DefaultNamespace = defaultNamespace;
            return this;
        }

        public XmlSerializationOptions AddNamespace(string prefix, string ns)
        {
            Namespaces.Add(prefix, ns);
            return this;
        }

        public XmlSerializationOptions Indent()
        {
            ShouldIndent = true;
            return this;
        }

        public XmlSerializationOptions SetRootElementName(string rootElementName)
        {
            RootElementName = rootElementName;
            return this;
        }

        public XmlSerializationOptions AlwaysEmitTypes()
        {
            ShouldAlwaysEmitTypes = true;
            return this;
        }

        public XmlSerializationOptions DisableRedact()
        {
            ShouldRedact = false;
            return this;
        }

        public XmlSerializationOptions ShouldTreatEmptyElementAsString()
        {
            TreatEmptyElementAsString = true;
            return this;
        }
    }
}