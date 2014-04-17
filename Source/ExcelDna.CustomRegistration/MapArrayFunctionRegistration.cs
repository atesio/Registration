﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq.Expressions;
using System.Text;
using ExcelDna.Integration;

[assembly: InternalsVisibleTo("ExcelDna.CustomRegistration.Test")]
namespace ExcelDna.CustomRegistration
{
    /// <summary>
    /// Defines an attribute to identify functions that define a mapping array function using enumerables and property reflection.
    /// </summary>
    public class ExcelMapArrayFunctionAttribute : ExcelFunctionAttribute
    {
    }

    public static class MapArrayFunctionRegistration
    {
        /// <summary>
        /// Modifies RegistrationEntries which have methods with IEnumerable signatures,
        /// allowing them to be converted to and from Excel Ranges (i.e. object[,]).
        /// The first row in each Range contains column headers, which are mapped to and from
        /// the public properties of the enumerated types.
        /// Currently just supports methods with signature IEnumerable<typeparamref name="T"/> -> IEnumerable<typeparamref name="U"/>
        /// E.g.
        ///     struct Output { int Out; }
        ///     struct Input  { int In1; int In2; }
        ///     IEnumerable<typeparamref name="Output"/> MyFunc(IEnumerable<typeparamref name="Input"/>) { ... }
        /// In Excel, use an Array Formula, e.g.
        ///       | A       B       C       
        ///     --+-------------------------
        ///     1 | In1     In2     {=MyFunc(A1:B3)} -> Out
        ///     2 | 1.0     2.0     {=MyFunc(A1:B3)} -> 1.5
        ///     3 | 2.0     3.0     {=MyFunc(A1:B3)} -> 2.5
        /// </summary>
        public static IEnumerable<ExcelFunctionRegistration> ProcessMapArrayFunctions(
            this IEnumerable<ExcelFunctionRegistration> registrations)
        {
            foreach (var reg in registrations)
            {
                if (!(reg.FunctionAttribute is ExcelMapArrayFunctionAttribute))
                {
                    // Not considered at all
                    yield return reg;
                    continue;
                }

                // avoid manipulating reg until we're sure it can succeed
                LambdaExpression shimLambda;
                string functionDescription;
                string[] parameterDescriptions;

                try
                {
                    switch (reg.FunctionLambda.Parameters.Count)
                    {
                        case 1:
                        {
                            Func<object[,], object[,]> shim = reg.FunctionLambda.MakeObjectArrayShim1To1();
                            shimLambda = (Expression<Func<object[,], object[,]>>) (input => shim(input));
                            break;
                        }
                        case 2:
                        {
                            Func<object[,], object[,], object[,]> shim = reg.FunctionLambda.MakeObjectArrayShim2To1();
                            shimLambda = (Expression<Func<object[,], object[,], object[,]>>)((input1, input2) => shim(input1, input2));
                            break;
                        }
                        default:
                            throw new ArgumentException(string.Format("Unsupported target expression with {0} parameters", reg.FunctionLambda.Parameters.Count), "reg");
                    }

                    // replace the Function attribute, with a description of the output fields
                    functionDescription = "Returns an array, with header row containing:\n" + 
                        GetEnumerableRecordTypeHelpString(reg.FunctionLambda.ReturnType);

                    // replace the Argument description, with a description of the input fields
                    parameterDescriptions = new string[reg.FunctionLambda.Parameters.Count];
                    for (int param = 0; param != reg.FunctionLambda.Parameters.Count; ++param)
                    {
                        parameterDescriptions[param] = "Input array, with header row containing:\n" +
                            GetEnumerableRecordTypeHelpString(reg.FunctionLambda.Parameters[param].Type);
                    }

                    if (parameterDescriptions.GetLength(0) != reg.ParameterRegistrations.Count)
                        throw new InvalidOperationException(
                            string.Format("Unexpected number of parameter registrations {0} vs {1}",
                                parameterDescriptions.GetLength(0), reg.ParameterRegistrations.Count));

                    // all ok - modify the registration
                    reg.FunctionLambda = shimLambda;
                    if(String.IsNullOrEmpty(reg.FunctionAttribute.Description))
                        reg.FunctionAttribute.Description = functionDescription;
                    for (int param = 0; param != reg.ParameterRegistrations.Count; ++param)
                    {
                        if (String.IsNullOrEmpty(reg.ParameterRegistrations[param].ArgumentAttribute.Description))
                            reg.ParameterRegistrations[param].ArgumentAttribute.Description =
                                parameterDescriptions[param];
                    }
                }
                catch
                {
                    // failed to shim, just register original
                }
                yield return reg;
            }
        }

        /// <summary>
        /// Wrapper for Convert.ChangeType which understands Excel's use of doubles as OADates.
        /// </summary>
        /// <param name="from">Excel object to convert into a different .NET type</param>
        /// <param name="toType">Type to convert to</param>
        /// <returns>Converted object</returns>
        private static object ConvertFromExcelObject(object from, Type toType)
        {
            // special case when converting from Excel double to DateTime
            // no need for special case in reverse, because Excel-DNA understands a DateTime object
            if (toType == typeof(DateTime) && from is double)
            {
                return DateTime.FromOADate((double)from);
            }
            return Convert.ChangeType(from, toType);
        }

        /// <summary>
        /// Returns a description string which is used in the Excel function dialog.
        /// E.g. IEnumerable<typeparamref name="T"/> produces a string with the public properties
        /// of type T.
        /// This really belongs in a View class somewhere...
        /// </summary>
        /// <param name="enumerableType">Enumerable type for which the help string is required</param>
        /// <returns>Formatted help string, complete with whitespace and newlines</returns>
        private static string GetEnumerableRecordTypeHelpString(Type enumerableType)
        {
            PropertyInfo[] recordProperties;
            GetEnumerableRecordType(enumerableType, out recordProperties);
            var headerFields = new StringBuilder();
            string delim = "";
            foreach (var prop in recordProperties)
            {
                headerFields.Append(delim);
                headerFields.Append(prop.Name);
                delim = ",";
            }
            return headerFields.ToString();
        }

        /// <summary>
        /// Returns the Record type which an enumerator yields.
        /// A valid record type must have at least 1 public property.
        /// </summary>
        /// <param name="enumerableType">Enumerable type to reflect</param>
        /// <param name="recordProperties">List of the yielded type's public properties</param>
        /// <returns>The enumerable's yielded type</returns>
        private static Type GetEnumerableRecordType(Type enumerableType, out PropertyInfo[] recordProperties)
        {
            if (!enumerableType.IsGenericType || enumerableType.Name != "IEnumerable`1")
                throw new ArgumentException(string.Format("Type {0} is not IEnumerable<>", enumerableType), "enumerableType");

            var typeArgs = enumerableType.GetGenericArguments();
            if (typeArgs.Length != 1)
                throw new ArgumentException(string.Format("Unexpected {0} generic args for type {1}",
                    typeArgs.Length, enumerableType.Name));

            Type recordType = typeArgs[0];
            recordProperties =
                recordType.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).
                    OfType<PropertyInfo>().ToArray();
            if (recordProperties.Length == 0)
                throw new ArgumentException(string.Format("Unsupported record type {0} with 0 public properties",
                    recordType.Name));

            return recordType;
        }

        /// <summary>
        /// Function which creates a shim for a target method.
        /// The target method is expected to take an enumerable of one type, and return an enumerable of another type.
        /// The shim is a delegate, which takes a 2d array of objects, and returns a 2d array of objects.
        /// The first row of each array defines the field names, which are mapped to the public properties of the
        /// input and return types.
        /// </summary>
        /// <param name="targetMethod"></param>
        /// <param name="inputFields"></param>
        /// <param name="returnFields"></param>
        /// <returns></returns>
        internal static Func<object[,], object[,]> MakeObjectArrayShim1To1(this LambdaExpression targetMethod)
        {
            // at the moment we only support enumerable<T> -> enumerable<U>. Check the target method has exactly 1 input parameter.
            if (targetMethod.Parameters.Count != 1)
                throw new ArgumentException(string.Format("Unsupported target expression with {0} parameters", targetMethod.Parameters.Count), "targetMethod");

            // validate and extract property info for the input and return types
            PropertyInfo[] inputRecordProperties, returnRecordProperties;
            var inputRecordType = GetEnumerableRecordType(targetMethod.Parameters.First().Type, out inputRecordProperties);
            var returnRecordType = GetEnumerableRecordType(targetMethod.ReturnType, out returnRecordProperties);

            // create the delegate, object[,] => object[,]
            Func<object[,], object[,]> shimMethod = inputObjectArray =>
            {
                // validate the input array
                if (inputObjectArray.GetLength(0) == 0)
                    throw new ArgumentException();
                int nInputRows = inputObjectArray.GetLength(0) - 1;
                int nInputCols = inputObjectArray.GetLength(1);

                // Decorate the input record properties with the matching
                // column indices from the input array. We have to do this each time
                // the shim is invoked to map column headers dynamically.
                // Would this be better as a SelectMany?
                var inputPropertyCols = inputRecordProperties.Select(propInfo =>
                {
                    int colIndex = -1;

                    for (int inputCol = 0; inputCol != nInputCols; ++inputCol)
                    {
                        var colName = inputObjectArray[0, inputCol] as string;
                        if (colName == null)
                            continue;

                        if (propInfo.Name.Equals(colName, StringComparison.OrdinalIgnoreCase))
                        {
                            colIndex = inputCol;
                            break;
                        }
                    }

                    return Tuple.Create(propInfo, colIndex);
                }).ToArray();

                // create a sequence of InputRecords
                var inputRecordArray = Array.CreateInstance(inputRecordType, nInputRows);

                // populate it
                for (int row = 0; row != nInputRows; ++row)
                {
                    object inputRecord;
                    try
                    {
                        // try using constructor which takes parameters in their declared order
                        inputRecord = Activator.CreateInstance(inputRecordType,
                            inputPropertyCols.Select(
                                prop =>
                                    ConvertFromExcelObject(inputObjectArray[row + 1, prop.Item2],
                                        prop.Item1.PropertyType)).ToArray());
                    }
                    catch (MissingMethodException)
                    {
                        // try a different way... default constructor and then set properties
                        inputRecord = Activator.CreateInstance(inputRecordType);

                        // populate the record
                        foreach (var prop in inputPropertyCols)
                            prop.Item1.SetValue(inputRecord,
                                ConvertFromExcelObject(inputObjectArray[row + 1, prop.Item2],
                                    prop.Item1.PropertyType), null);
                    }

                    inputRecordArray.SetValue(inputRecord, row);
                }

                // invoke the method
                var returnRecordSequence = targetMethod.Compile().DynamicInvoke(inputRecordArray);

                // turn it ito an Array<OutputRecordType>
                var genericToArray =
                    typeof (Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                        .First(mi => mi.Name == "ToArray");
                if (genericToArray == null)
                    throw new InvalidOperationException("Internal error. Failed to find Enumerable.ToArray");
                var toArray = genericToArray.MakeGenericMethod(returnRecordType);
                var returnRecordArray = toArray.Invoke(null, new object[] {returnRecordSequence}) as Array;
                if (returnRecordArray == null)
                    throw new InvalidOperationException("Internal error. Failed to convert return record to Array");

                // create a return object array and populate the first row
                var nReturnRows = returnRecordArray.Length;
                var returnObjectArray = new object[nReturnRows + 1, returnRecordProperties.Length];
                for (int outputCol = 0; outputCol != returnRecordProperties.Length; ++outputCol)
                    returnObjectArray[0, outputCol] = returnRecordProperties[outputCol].Name;

                // iterate through the entire array and populate the output
                for (int returnRow = 0; returnRow != nReturnRows; ++returnRow)
                {
                    for (int returnCol = 0; returnCol != returnRecordProperties.Length; ++returnCol)
                    {
                        returnObjectArray[returnRow + 1, returnCol] = returnRecordProperties[returnCol].
                            GetValue(returnRecordArray.GetValue(returnRow), null);
                    }
                }

                return returnObjectArray;
            };
            return shimMethod;
        }

        /// <summary>
        /// Function which creates a shim for a target method.
        /// The target method is expected to take an enumerable of one type, and return an enumerable of another type.
        /// The shim is a delegate, which takes a 2d array of objects, and returns a 2d array of objects.
        /// The first row of each array defines the field names, which are mapped to the public properties of the
        /// input and return types.
        /// </summary>
        /// <param name="targetMethod"></param>
        /// <param name="inputFields"></param>
        /// <param name="returnFields"></param>
        /// <returns></returns>
        internal static Func<object[,], object[,], object[,]> MakeObjectArrayShim2To1(this LambdaExpression targetMethod)
        {
            // at the moment we only support enumerable<T> enumerable<U> -> enumerable<V>. Check the target method has exactly 2 input parameters.
            if (targetMethod.Parameters.Count != 2)
                throw new ArgumentException(string.Format("Unsupported target expression with {0} parameters", targetMethod.Parameters.Count), "targetMethod");

            // validate and extract property info for the input and return types
            var inputRecordProperties = new PropertyInfo[2][];
            PropertyInfo[] returnRecordProperties;
            var inputRecordType = new[]
            {
                GetEnumerableRecordType(targetMethod.Parameters[0].Type, out inputRecordProperties[0]),
                GetEnumerableRecordType(targetMethod.Parameters[1].Type, out inputRecordProperties[1])
            };
            var returnRecordType = GetEnumerableRecordType(targetMethod.ReturnType, out returnRecordProperties);

            // create the delegate, object[,] object[,] -> object[,]
            Func<object[,], object[,], object[,]> shimMethod = (input1ObjectArray, input2ObjectArray) =>
            {
                var inputObjectArray = new[]
                {
                    input1ObjectArray,
                    input2ObjectArray
                };

                var inputRecordArray = new Array[2];

                for (int i = 0; i != 2; ++i)
                {
                    if (inputObjectArray[i].GetLength(0) == 0)
                        throw new ArgumentException();

                    // extract nrows and ncols for each input array
                    int nInputRows = inputObjectArray[i].GetLength(0) - 1;
                    int nInputCols = inputObjectArray[i].GetLength(1);

                    // Decorate the input record properties with the matching
                    // column indices from the input array. We have to do this each time
                    // the shim is invoked to map column headers dynamically.
                    // Would this be better as a SelectMany?
                    var inputPropertyCols = inputRecordProperties[i].Select(propInfo =>
                    {
                        int colIndex = -1;

                        for (int inputCol = 0; inputCol != nInputCols; ++inputCol)
                        {
                            var colName = inputObjectArray[i][0, inputCol] as string;
                            if (colName == null)
                                continue;

                            if (propInfo.Name.Equals(colName, StringComparison.OrdinalIgnoreCase))
                            {
                                colIndex = inputCol;
                                break;
                            }
                        }
                        return Tuple.Create(propInfo, colIndex);
                    }).ToArray();

                    // create a sequence of InputRecords
                    inputRecordArray[i] = Array.CreateInstance(inputRecordType[i], nInputRows);

                    // populate it
                    for (int row = 0; row != nInputRows; ++row)
                    {
                        object inputRecord;
                        try
                        {
                            // try using constructor which takes parameters in their declared order
                            inputRecord = Activator.CreateInstance(inputRecordType[i],
                                inputPropertyCols.Select(
                                    prop =>
                                        ConvertFromExcelObject(inputObjectArray[i][row + 1, prop.Item2],
                                            prop.Item1.PropertyType)).ToArray());
                        }
                        catch (MissingMethodException)
                        {
                            // try a different way... default constructor and then set properties
                            inputRecord = Activator.CreateInstance(inputRecordType[i]);

                            // populate the record
                            foreach (var prop in inputPropertyCols)
                                prop.Item1.SetValue(inputRecord,
                                    ConvertFromExcelObject(inputObjectArray[i][row + 1, prop.Item2],
                                        prop.Item1.PropertyType), null);
                        }

                        inputRecordArray[i].SetValue(inputRecord, row);
                    }
                }

                // invoke the method
                var returnRecordSequence = targetMethod.Compile().DynamicInvoke(inputRecordArray[0], inputRecordArray[1]);

                // turn it ito an Array<OutputRecordType>
                var genericToArray =
                    typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                        .First(mi => mi.Name == "ToArray");
                if (genericToArray == null)
                    throw new InvalidOperationException("Internal error. Failed to find Enumerable.ToArray");
                var toArray = genericToArray.MakeGenericMethod(returnRecordType);
                var returnRecordArray = toArray.Invoke(null, new object[] { returnRecordSequence }) as Array;
                if (returnRecordArray == null)
                    throw new InvalidOperationException("Internal error. Failed to convert return record to Array");

                // create a return object array and populate the first row
                var nReturnRows = returnRecordArray.Length;
                var returnObjectArray = new object[nReturnRows + 1, returnRecordProperties.Length];
                for (int outputCol = 0; outputCol != returnRecordProperties.Length; ++outputCol)
                    returnObjectArray[0, outputCol] = returnRecordProperties[outputCol].Name;

                // iterate through the entire array and populate the output
                for (int returnRow = 0; returnRow != nReturnRows; ++returnRow)
                {
                    for (int returnCol = 0; returnCol != returnRecordProperties.Length; ++returnCol)
                    {
                        returnObjectArray[returnRow + 1, returnCol] = returnRecordProperties[returnCol].
                            GetValue(returnRecordArray.GetValue(returnRow), null);
                    }
                }

                return returnObjectArray;
            };
            return shimMethod;
        }
    }
}
