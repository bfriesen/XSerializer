﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using NUnit.Framework;

namespace XSerializer.Tests
{
    public abstract class ObjectToXml
    {
        [TestCaseSource("TestCaseData")]
        public void SerializesCorrectly(object instance, Type type, string expectedXml)
        {
            var customSerializer = GetSerializer(type);

            var customXml = customSerializer.SerializeObject(instance, Encoding.UTF8, Formatting.Indented, new TestSerializeOptions(shouldAlwaysEmitTypes:AlwaysEmitTypes));

            Console.WriteLine("Expected XML:");
            Console.WriteLine(expectedXml);
            Console.WriteLine();
            Console.WriteLine("Actual XML:");
            Console.WriteLine(customXml);

            Assert.That(customXml, Is.EqualTo(expectedXml));
        }

        protected virtual IXmlSerializer GetSerializer(Type type)
        {
            return CustomSerializer.GetSerializer(type, TestXmlSerializerOptions.Empty);
        }

        protected virtual bool AlwaysEmitTypes
        {
            get { return false; }
        }

        protected IEnumerable<TestCaseData> TestCaseData
        {
            get
            {
                return GetTestCaseData().Select(testCaseData =>
                {
                    if (string.IsNullOrWhiteSpace(testCaseData.TestName))
                    {
                        var instanceType = testCaseData.Arguments[0].GetType();
                        var type = (Type)testCaseData.Arguments[1];

                        return testCaseData.SetName(type == instanceType ? type.Name : string.Format("{0} as {1}", instanceType.Name, type.Name));
                    }

                    return testCaseData;
                });
            }
        }

        protected abstract IEnumerable<TestCaseData> GetTestCaseData();
    }
}