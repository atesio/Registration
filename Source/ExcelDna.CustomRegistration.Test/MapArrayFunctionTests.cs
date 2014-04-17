﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace ExcelDna.CustomRegistration.Test
{
    [TestFixture]
    public class MapArrayFunctionTests
    {
        //////////////////////////////////////////////////////////////////////////////////////////////
        // Define 4 classes which we'll use as IEnumerable record types.
        #region Record Classes

        /// <summary>
        /// Struct, with initialising constructor
        /// </summary>
        public struct TestStructWithCtor
        {
            public TestStructWithCtor(double d, int i, string s, DateTime dt)
            {
                _d = d;
                _i = i;
                _s = s;
                _dt = dt;
            }

            private double _d;
            public double D { set { _d = value; } get { return _d; } }

            private int _i;
            public int I { set { _i = value; } get { return _i; } }

            private string _s;
            public string S { set { _s = value; } get { return _s; } }

            private DateTime _dt;
            public DateTime Dt { set { _dt = value; } get { return _dt; } }
        }

        /// <summary>
        /// Class, with initialising constructor, equivalent to F# Record type
        /// </summary>
        public class TestClassWithCtor
        {
            public TestClassWithCtor(double d, int i, string s, DateTime dt)
            {
                D = d;
                I = i;
                S = s;
                Dt = dt;
            }

            public double D { get; set; }
            public int I { get; set; }
            public string S { get; set; }
            public DateTime Dt { get; set; }
        }

        /// <summary>
        /// Struct, with no initialising constructor
        /// </summary>
        public struct TestStructDefaultCtor
        {
            public double D { get; set; }
            public int I { get; set; }
            public string S { get; set; }
            public DateTime Dt { get; set; }
        }

        /// <summary>
        /// Class, with no initialising constructor
        /// </summary>
        public class TestClassDefaultCtor
        {
            public double D { get; set; }
            public int I { get; set; }
            public string S { get; set; }
            public DateTime Dt { get; set; }
        }

        #endregion

        //////////////////////////////////////////////////////////////////////////////////////////////
        // Define some methods which we want to create Shims for, to use in Excel
        // Signature is IEnumerable<T> -> IEnumerable<T>
        #region Target Methods for Shim

        /// <summary>
        /// A method for us to test
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static IEnumerable<T> Reverse<T>(IEnumerable<T> input)
        {
            return input.Reverse();
        }

        public static IEnumerable<T> CombineAndReverse<T>(IEnumerable<T> input1, IEnumerable<T> input2)
        {
            return input1.Concat(input2).Reverse();
        }

        public static MethodInfo[] MethodsToShim1To1 =
        {
            typeof (MapArrayFunctionTests).GetMethod("Reverse").MakeGenericMethod(typeof (TestStructWithCtor)),
            typeof (MapArrayFunctionTests).GetMethod("Reverse").MakeGenericMethod(typeof (TestClassWithCtor)),
            typeof (MapArrayFunctionTests).GetMethod("Reverse").MakeGenericMethod(typeof (TestStructDefaultCtor)),
            typeof (MapArrayFunctionTests).GetMethod("Reverse").MakeGenericMethod(typeof (TestClassDefaultCtor))
        };

        public static MethodInfo[] MethodsToShim2To1 =
        {
            typeof (MapArrayFunctionTests).GetMethod("CombineAndReverse").MakeGenericMethod(typeof(TestStructWithCtor)),
            typeof (MapArrayFunctionTests).GetMethod("CombineAndReverse").MakeGenericMethod(typeof(TestClassWithCtor)),
            typeof (MapArrayFunctionTests).GetMethod("CombineAndReverse").MakeGenericMethod(typeof(TestStructDefaultCtor)),
            typeof (MapArrayFunctionTests).GetMethod("CombineAndReverse").MakeGenericMethod(typeof(TestClassDefaultCtor)),
        };

        #endregion

        //////////////////////////////////////////////////////////////////////////////////////////////
        #region Tests

        private static readonly object[,] _inputData =
        {
            // input data can have fields in any order, with different case
            { "I", "S", "D", "DT" },

            { 123, "123", 123.0, new DateTime(2014,03,10,17,40,21) },
            { 456, "456", 456.0, new DateTime(2001,11,23,22,45,00) },
            { 56789.3, 56789, 56789, 41910.0 }  // Test conversion from non-regular types
        };
        private static readonly object[,] _expectedOutputData1To1 = 
        {
            // output data fields are determined by type, not data
            { "D", "I", "S", "Dt" },

            { 56789.0, 56789, "56789", DateTime.FromOADate(41910.0) },
            { 456.0, 456, "456", new DateTime(2001,11,23,22,45,00) },
            { 123.0, 123, "123", new DateTime(2014,03,10,17,40,21) }
        };
        private static readonly object[,] _expectedOutputData2To1 = 
        {
            // output data fields are determined by type, not data
            { "D", "I", "S", "Dt" },

            { 56789.0, 56789, "56789", DateTime.FromOADate(41910.0) },
            { 456.0, 456, "456", new DateTime(2001,11,23,22,45,00) },
            { 123.0, 123, "123", new DateTime(2014,03,10,17,40,21) },
            { 56789.0, 56789, "56789", DateTime.FromOADate(41910.0) },
            { 456.0, 456, "456", new DateTime(2001,11,23,22,45,00) },
            { 123.0, 123, "123", new DateTime(2014,03,10,17,40,21) }
        };

        [Test, TestCaseSource("MethodsToShim1To1")]
        public void TestMapArrayRegistrations1To1(MethodInfo methodInfo)
        {
            ////////////////////////////////////////////
            // arrange

            Assert.IsNotNull(methodInfo);

            // wrap the method in a registrationentry
            var registration = new ExcelFunctionRegistration(methodInfo)
            {
                FunctionAttribute = new ExcelMapArrayFunctionAttribute()
            };

            //////////////////////////////////////////
            // act

            var processed = Enumerable.Repeat(registration, 1).ProcessMapArrayFunctions().ToList();

            //////////////////////////////////////////
            // assert

            Assert.AreEqual(1, processed.Count());
            var processedRegistration = processed.First();

            var functionDescription = registration.FunctionAttribute.Description;
            for (int col = 0; col != _expectedOutputData1To1.GetLength(1); ++col)
            {
                var colHeader = _expectedOutputData1To1[0, col] as string;
                Assert.IsNotNull(colHeader);
                var regex = new Regex(@"\b" + colHeader + @"\b", RegexOptions.IgnoreCase);
                Assert.IsTrue(regex.Match(functionDescription).Success, functionDescription);
            }

            Assert.AreEqual(1, registration.ParameterRegistrations.Count);
            var parameterDescription = registration.ParameterRegistrations[0].ArgumentAttribute.Description;
            for (int col = 0; col != _inputData.GetLength(1); ++col)
            {
                var colHeader = _inputData[0, col] as string;
                Assert.IsNotNull(colHeader);
                var regex = new Regex(@"\b" + colHeader + @"\b", RegexOptions.IgnoreCase);
                Assert.IsTrue(regex.Match(parameterDescription).Success, parameterDescription);
            }

            //////////////////////////////////////////
            // act

            // invoke the delegate
            var output = processedRegistration.FunctionLambda.Compile().DynamicInvoke(_inputData);

            //////////////////////////////////////////
            // assert

            Assert.IsNotNull(output);
            Assert.AreEqual(_expectedOutputData1To1, output);
        }

        [Test, TestCaseSource("MethodsToShim2To1")]
        public void TestMapArrayRegistrations2To1(MethodInfo methodInfo)
        {
            ////////////////////////////////////////////
            // arrange

            Assert.IsNotNull(methodInfo);

            // wrap the method in a registrationentry
            var registration = new ExcelFunctionRegistration(methodInfo);
            registration.FunctionAttribute = new ExcelMapArrayFunctionAttribute();

            //////////////////////////////////////////
            // act

            var processed = Enumerable.Repeat(registration, 1).ProcessMapArrayFunctions().ToList();

            //////////////////////////////////////////
            // assert

            Assert.AreEqual(1, processed.Count());
            var processedRegistration = processed.First();

            var functionDescription = registration.FunctionAttribute.Description;
            for (int col = 0; col != _expectedOutputData1To1.GetLength(1); ++col)
            {
                var colHeader = _expectedOutputData2To1[0, col] as string;
                Assert.IsNotNull(colHeader);
                var regex = new Regex(@"\b" + colHeader + @"\b", RegexOptions.IgnoreCase);
                Assert.IsTrue(regex.Match(functionDescription).Success, functionDescription);
            }

            Assert.AreEqual(2, registration.ParameterRegistrations.Count);
            var parameterDescription0 = registration.ParameterRegistrations[0].ArgumentAttribute.Description;
            var parameterDescription1 = registration.ParameterRegistrations[0].ArgumentAttribute.Description;
            for (int col = 0; col != _inputData.GetLength(1); ++col)
            {
                var colHeader = _inputData[0, col] as string;
                Assert.IsNotNull(colHeader);
                var regex = new Regex(@"\b" + colHeader + @"\b", RegexOptions.IgnoreCase);
                Assert.IsTrue(regex.Match(parameterDescription0).Success, parameterDescription0);
                Assert.IsTrue(regex.Match(parameterDescription1).Success, parameterDescription1);
            }

            //////////////////////////////////////////
            // act

            // invoke the delegate
            var output = processedRegistration.FunctionLambda.Compile().DynamicInvoke(_inputData, _inputData);

            //////////////////////////////////////////
            // assert

            Assert.IsNotNull(output);
            Assert.AreEqual(_expectedOutputData2To1, output);
        }

        #endregion
    }
}
