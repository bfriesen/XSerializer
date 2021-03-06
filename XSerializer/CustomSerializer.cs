﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Serialization;

namespace XSerializer
{
    public class CustomSerializer
    {
        private static readonly ConcurrentDictionary<int, IXmlSerializer> _serializerCache = new ConcurrentDictionary<int, IXmlSerializer>();
        private static readonly object _serializerCacheLocker = new object();

        public static IXmlSerializer GetSerializer(Type type, IXmlSerializerOptions options)
        {
            IXmlSerializer serializer;
            var key = XmlSerializerFactory.Instance.CreateKey(type, options);

            if (!_serializerCache.TryGetValue(key, out serializer))
            {
                lock (_serializerCacheLocker)
                {
                    if (!_serializerCache.TryGetValue(key, out serializer))
                    {
                        try
                        {
                            serializer = (IXmlSerializer)Activator.CreateInstance(typeof(CustomSerializer<>).MakeGenericType(type), options);
                        }
                        catch (TargetInvocationException ex) // True exception gets masked due to reflection. Preserve stacktrace and rethrow
                        {
                            PreserveStackTrace(ex.InnerException);
                            throw ex.InnerException;
                        }

                        _serializerCache[key] = serializer;
                    }
                }
            }

            return serializer;
        }

        //Stackoverflow is awesome
        private static void PreserveStackTrace(Exception e)
        {
            var ctx = new StreamingContext(StreamingContextStates.CrossAppDomain);
            var mgr = new ObjectManager(null, ctx);
            var si = new SerializationInfo(e.GetType(), new FormatterConverter());

            e.GetObjectData(si, ctx);
            mgr.RegisterObject(e, 1, si); // prepare for SetObjectData
            mgr.DoFixups(); // ObjectManager calls SetObjectData

            // voila, e is unmodified save for _remoteStackTraceString
        }
    }

    public class CustomSerializer<T> : CustomSerializer, IXmlSerializer<T>
    {
        private readonly IXmlSerializerOptions _options;
        private readonly Dictionary<Type, SerializableProperty[]> _serializablePropertiesMap = new Dictionary<Type, SerializableProperty[]>();

        public CustomSerializer(IXmlSerializerOptions options)
        {
            var type = typeof(T);
            AssertValidHeirarchy(type);

            _options = options.WithAdditionalExtraTypes(
                type.GetCustomAttributes(typeof(XmlIncludeAttribute), true)
                    .Cast<XmlIncludeAttribute>()
                    .Select(a => a.Type));

            if (string.IsNullOrWhiteSpace(_options.RootElementName))
            {
                _options = _options.WithRootElementName(GetRootElement(type));
            }

            var types = _options.ExtraTypes.ToList();
            if (!type.IsInterface && !type.IsAbstract)
            {
                types.Insert(0, type);
            }

            _serializablePropertiesMap =
                types.Distinct().ToDictionary(
                    t => t,
                    t =>
                        t.GetProperties()
                        .Where(p => p.IsSerializable())
                        .Select(p => new SerializableProperty(p, _options))
                        .OrderBy(p => p.NodeType)
                        .ToArray());
        }

        private void AssertValidHeirarchy(Type type)
        {
            if (type.BaseType == typeof (object)) return;

            var properties = type.GetProperties();

            foreach (var property in properties)
            {
                var derivedXmlElement = GetAttribute<XmlElementAttribute>(property);
                var derivedXmlAttribute = GetAttribute<XmlAttributeAttribute>(property);
                var baseProperty = GetBaseProperty(property);
                var hasBaseProperty = baseProperty != null;

                if (hasBaseProperty)
                {
                    AssertPropertyHeirarchy(baseProperty, derivedXmlElement, derivedXmlAttribute);
                }

                if (derivedXmlAttribute != null && !hasBaseProperty)
                {
                    if (string.IsNullOrWhiteSpace(derivedXmlAttribute.AttributeName))
                    {
                        throw new InvalidOperationException("XmlAttribute must have a value.");
                    }
                }

                if (derivedXmlElement != null && !hasBaseProperty)
                {
                    if (string.IsNullOrWhiteSpace(derivedXmlElement.ElementName))
                    {
                        throw new InvalidOperationException("XmlElement must have a value.");
                    }
                }
            }
        }

        private void AssertPropertyHeirarchy(PropertyInfo baseProperty, XmlElementAttribute derivedXmlElement, XmlAttributeAttribute derivedXmlAttribute)
        {
            var baseXmlElement = GetAttribute<XmlElementAttribute>(baseProperty);
            var baseXmlAttribute = GetAttribute<XmlAttributeAttribute>(baseProperty);

            if (baseXmlAttribute != null)
            {
                if (derivedXmlElement != null)
                {
                    throw new InvalidOperationException("Derived XmlElement cannot override Base XmlAttribute.");
                }

                if (derivedXmlAttribute != null)
                {
                    if (string.IsNullOrWhiteSpace(derivedXmlAttribute.AttributeName))
                    {
                        if (!string.IsNullOrWhiteSpace(baseXmlAttribute.AttributeName))
                        {
                            throw new InvalidOperationException("Overridden property must have non-empty Attribute.");
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(baseXmlAttribute.AttributeName) && !string.IsNullOrWhiteSpace(derivedXmlAttribute.AttributeName))
                        {
                            throw new InvalidOperationException("Virtual property must have non-empty XmlAttribute.");
                        }

                        if (!string.IsNullOrWhiteSpace(baseXmlAttribute.AttributeName) && baseXmlAttribute.AttributeName != derivedXmlAttribute.AttributeName)
                        {
                            throw new InvalidOperationException("Base property and dervied property must have the same attribute.");
                        }
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(baseXmlAttribute.AttributeName))
                    {
                        throw new InvalidOperationException("Overridden property must override XmlAttribute");
                    }

                    if (string.IsNullOrWhiteSpace(baseXmlAttribute.AttributeName)) // && string.IsNullOrWhiteSpace(derivedXmlAttribute.AttributeName)
                    {
                        throw new InvalidOperationException("Virtual property must have non-empty XmlAttribute.");
                    }
                }
            }
            else
            {
                if (derivedXmlAttribute != null)
                {
                    throw new InvalidOperationException("Virtual property must have non-empty XmlAttribute.");
                }
            }

            if (baseXmlElement != null)
            {
                if (derivedXmlAttribute != null)
                {
                    throw new InvalidOperationException("Derived XmlAttribute cannot override Base XmlElement.");
                }

                if (derivedXmlElement != null)
                {
                    if (!string.IsNullOrWhiteSpace(baseXmlElement.ElementName))
                    {
                        if (string.IsNullOrWhiteSpace(derivedXmlElement.ElementName))
                        {
                            throw new InvalidOperationException("Cannot have non-empty Xml Element.");
                        }
                        
                        if (derivedXmlElement.ElementName != baseXmlElement.ElementName)
                        {
                            throw new InvalidOperationException("Dervied Element cannot be different from Base element.");
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(derivedXmlElement.ElementName))
                        {
                            throw new InvalidOperationException("Base element cannot be empty.");
                        }
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(baseXmlElement.ElementName))
                    {
                        throw new InvalidOperationException("Dervied property must override base property XmlElement.");
                    }
                }
            }
            else
            {
                if (derivedXmlElement != null && !string.IsNullOrWhiteSpace(derivedXmlElement.ElementName))
                {
                    throw new InvalidOperationException("Base property must have XmlElement.");
                }
            }
        }

        private static PropertyInfo GetBaseProperty(PropertyInfo propertyInfo)
        {
            var method = propertyInfo.GetAccessors(true)[0];
            if (method == null)
                return null;

            var baseMethod = method.GetBaseDefinition();

            if (baseMethod == method)
                return propertyInfo;

            const BindingFlags allProperties = BindingFlags.Instance | BindingFlags.Public
                                               | BindingFlags.NonPublic | BindingFlags.Static;

            var arguments = propertyInfo.GetIndexParameters().Select(p => p.ParameterType).ToArray();

            Debug.Assert(baseMethod.DeclaringType != null);

            return baseMethod.DeclaringType.GetProperty(propertyInfo.Name, allProperties,
                null, propertyInfo.PropertyType, arguments, null);
        }

        private TAttribute GetAttribute<TAttribute>(PropertyInfo property) where TAttribute: Attribute
        {
            return property.GetCustomAttributes(typeof(TAttribute), false).FirstOrDefault() as TAttribute;
        }

        private static string GetRootElement(Type type)
        {
            var xmlRootAttribute = (XmlRootAttribute)type.GetCustomAttributes(typeof(XmlRootAttribute), true).FirstOrDefault();
            if (xmlRootAttribute != null && !string.IsNullOrWhiteSpace(xmlRootAttribute.ElementName))
            {
                return xmlRootAttribute.ElementName;
            }
            return type.Name;
        }

        public void Serialize(SerializationXmlTextWriter writer, T instance, ISerializeOptions options)
        {
            if (instance == null)
            {
                return;
            }

            writer.WriteStartDocument();
            writer.WriteStartElement(_options.RootElementName);
            writer.WriteDefaultNamespaces();

            if (!string.IsNullOrWhiteSpace(_options.DefaultNamespace))
            {
                writer.WriteAttributeString("xmlns", null, null, _options.DefaultNamespace);
            }

            var instanceType = instance.GetType();

            if (typeof(T).IsInterface || typeof(T).IsAbstract || typeof(T) != instanceType)
            {
                writer.WriteAttributeString("xsi", "type", null, instance.GetType().GetXsdType());
            }

            if (instanceType.IsPrimitiveLike() || instanceType.IsNullablePrimitiveLike())
            {
                XmlTextSerializer.GetSerializer(instanceType, _options.RedactAttribute).SerializeObject(writer, instance, options);
            }
            else
            {
                foreach (var property in _serializablePropertiesMap[instanceType])
                {
                    property.WriteValue(writer, instance, options);
                }
            }

            writer.WriteEndElement();
        }

        void IXmlSerializer.SerializeObject(SerializationXmlTextWriter writer, object instance, ISerializeOptions options)
        {
            Serialize(writer, (T)instance, options);
        }

        public T Deserialize(XmlReader reader)
        {
            T instance = default(T);
            var hasInstanceBeenCreated = false;

            bool shouldIssueRead;

            do
            {
                shouldIssueRead = true;

                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (reader.Name == _options.RootElementName)
                        {
                            if (!typeof(T).IsPrimitiveLike())
                            {
                                instance = CreateInstance(reader);
                                hasInstanceBeenCreated = true;

                                var setPropertyActions = new List<Action>();

                                while (reader.MoveToNextAttribute())
                                {
                                    var property =
                                        _serializablePropertiesMap[instance.GetType()]
                                            .SingleOrDefault(p => p.NodeType == NodeType.Attribute && p.Name == reader.Name);
                                    if (property != null)
                                    {
                                        setPropertyActions.Add(() => property.ReadValue(reader, instance));
                                    }
                                }

                                setPropertyActions.ForEach(action => action());

                                reader.MoveToElement();

                                if (reader.IsEmptyElement)
                                {
                                    return instance;
                                }
                            }
                            else if (reader.IsEmptyElement)
                            {
                                return default(T);
                            }
                        }
                        else
                        {
                            SetElementPropertyValue(reader, hasInstanceBeenCreated, instance, out shouldIssueRead);
                        }
                        break;
                    case XmlNodeType.Text:
                        if (typeof(T).IsPrimitiveLike())
                        {
                            instance = (T)XmlTextSerializer.GetSerializer(typeof(T), _options.RedactAttribute).DeserializeObject(reader);
                            hasInstanceBeenCreated = true;
                        }
                        else
                        {
                            SetTextNodePropertyValue(reader, hasInstanceBeenCreated, instance);
                        }
                        break;
                    case XmlNodeType.EndElement:
                        if (reader.Name == _options.RootElementName)
                        {
                            return CheckAndReturn(hasInstanceBeenCreated, instance);
                        }
                        break;
                }
            } while (reader.ReadIfNeeded(shouldIssueRead));

            throw new InvalidOperationException("Deserialization error: reached the end of the document without returning a value.");
        }

        object IXmlSerializer.DeserializeObject(XmlReader reader)
        {
            return Deserialize(reader);
        }

        // ReSharper disable UnusedParameter.Local
        private void SetElementPropertyValue(XmlReader reader, bool hasInstanceBeenCreated, T instance, out bool shouldIssueRead)
        {
            if (!hasInstanceBeenCreated)
            {
                throw new InvalidOperationException("Deserialization error: attempted to set a property value from an element before creating its object.");
            }

            var property = _serializablePropertiesMap[instance.GetType()].SingleOrDefault(p => reader.Name == p.Name);
            if (property != null)
            {
                property.ReadValue(reader, instance);
                shouldIssueRead = !property.ReadsPastLastElement;
            }
            else
            {
                shouldIssueRead = true;
            }
        }

        private void SetTextNodePropertyValue(XmlReader reader, bool hasInstanceBeenCreated, T instance)
        {
            if (!hasInstanceBeenCreated)
            {
                throw new InvalidOperationException("Deserialization error: attempted to set a property value from a text node before creating its object.");
            }

            var property = _serializablePropertiesMap[instance.GetType()].SingleOrDefault(p => p.NodeType == NodeType.Text);
            if (property != null)
            {
                property.ReadValue(reader, instance);
            }
        }

        private static T CheckAndReturn(bool hasInstanceBeenCreated, T instance)
        {
            if (!hasInstanceBeenCreated)
            {
                throw new InvalidOperationException("Deserialization error: attempted to return a deserialized instance before it was created.");
            }

            return instance;
        }
        // ReSharper restore UnusedParameter.Local

        private T CreateInstance(XmlReader reader)
        {
            T instance;
            var type = reader.GetXsdType<T>(_options.ExtraTypes);

            if (type != null)
            {
                instance = (T)Activator.CreateInstance(type); // TODO: cache into constructor func
            }
            else
            {
                instance = Activator.CreateInstance<T>(); // TODO: cache into constructor func
            }
            
            return instance;
        }
    }
}