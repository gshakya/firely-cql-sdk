﻿/* 
 * Copyright (c) 2023, NCQA and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.githubusercontent.com/FirelyTeam/firely-cql-sdk/main/LICENSE
 */

using Hl7.Cql.Conversion;
using Hl7.Cql.Primitives;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using Hl7.Fhir.Utility;
using System.Reflection;
using System.Text;
using M = Hl7.Fhir.Model;

namespace Hl7.Cql.Fhir
{
    /// <summary>
    /// Defines conversions between Cql and FHIR data models
    /// </summary>
    public static class FhirTypeConverter
    {
        /// <summary>
        /// Singleton for the default configuration of this TypeConverter
        /// </summary>
        public static readonly TypeConverter Default = Create(ModelInfo.ModelInspector);

        /// <summary>
        /// Allows for the creation of a converter with the specified model 
        /// </summary>
        /// <param name="model">the model</param>
        /// <returns>the type converter</returns>
        public static TypeConverter Create(ModelInspector model) =>
            TypeConverter
                .Create()
                .ConvertSystemTypes()
                .ConvertFhirToCqlPrimitives()
                .ConvertCqlPrimitivesToFhir()
                .ConvertCodeTypes(model);

        internal static TypeConverter ConvertFhirToCqlPrimitives(this TypeConverter converter)
        {
            HashSet<Type> toTypes = new();

            add((M.Integer p) => p.Value);
            add((M.FhirString p) => p.Value);
            add((M.FhirBoolean p) => p.Value);
            add((M.FhirDecimal p) => p.Value);
            add((M.Markdown p) => p.Value);
            add((M.Instant p) => p.Value);
            add((M.Code p) => p.Value);
            add((M.FhirUrl p) => p.Value);
            add((M.Date f) => f.TryToDate(out var date) ? new CqlDate(date!.Years!.Value, date.Months, date.Days) : null);
            add((M.Date f) => f.TryToDate(out var date) ? new CqlDateTime(date!.Years!.Value, date.Months, date.Days, 0, 0, 0, 0, 0, 0) : null);
            add((M.Date f) => f.ToString());
            add((M.Time f) => f.TryToTime(out var time) ? new CqlTime(time!.Hours!.Value, time.Minutes, time.Seconds, time.Millis, null, null) : null);
            add((M.Time f) => f.ToString());
            add((M.FhirDateTime f) => f.TryToDateTime(out var dt) ?
                new CqlDateTime(
                    dt!.Years!.Value, dt.Months,
                    dt.Days, dt.Hours, dt.Minutes, dt.Seconds, dt.Millis,
                    dt.HasOffset ? dt.Offset!.Value.Hours : null, dt.HasOffset ? dt.Offset!.Value.Minutes : null) : null);
            add((M.FhirDateTime f) => f.ToString());
            add((M.FhirDateTime f) => f.TryToDateTime(out var dt) ? new CqlDate(dt!.Years!.Value, dt.Months, dt.Days) : null);
            add((M.Quantity f) => new CqlQuantity(f.Value, f.Unit));
            add((M.Quantity f) => f.Value);
            add((M.Quantity f) => (int?)f.Value);
            add((M.Period f) => new CqlInterval<CqlDateTime>(
                converter.Convert<CqlDateTime>(f.StartElement), converter.Convert<CqlDateTime>(f.EndElement), lowClosed: true, highClosed: true));
            add((M.Period f) => new CqlInterval<CqlDate>(
                converter.Convert<CqlDate>(f.StartElement), converter.Convert<CqlDate>(f.EndElement), lowClosed: true, highClosed: true));
            add((M.Range f) => new CqlInterval<CqlQuantity>(
                    converter.Convert<CqlQuantity>(f.Low), converter.Convert<CqlQuantity>(f.High), lowClosed: true, highClosed: true));
            add((M.Range f) => new CqlInterval<decimal?>(converter.Convert<decimal?>(f.Low), converter.Convert<decimal?>(f.High), 
                lowClosed: true, highClosed: true));
            add((M.Range f) => new CqlInterval<int?>(converter.Convert<int?>(f.Low), converter.Convert<int?>(f.High), 
                lowClosed: true, highClosed: true));
            
            add((M.Id id) => id.Value);
            
            add((M.PositiveInt pi) => new M.Integer(pi.Value));
            add((M.PositiveInt pi) => pi.ToString());
            add((M.UnsignedInt ui) => new M.Integer(ui.Value));
            add((M.UnsignedInt ui) => ui.ToString());

            add((M.Canonical c) => c.ToString());

            addParametersToCqlPrimitivesConverters(toTypes);
            return converter;

            // Add a basic Fhir primitive->Cql primitive conversion
            void add<I, O>(Func<I, O> tos)
            {
                converter.AddConversion<I, O>(tos);
                if (!toTypes.Contains(typeof(O)))
                    toTypes.Add(typeof(O));
            }
            //CqlDateTime? changeDateTimeOffsetToCqlDateTime(DateTimeOffset? dt)
            //{
            //    if (dt.HasValue)
            //    {
            //    var dateTime = dt.Value;
            //        return new CqlDateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Millisecond, null, null);
            //    }
            //    return null;
            //}

            // Add a ParameterComponent->Cql primitive via the now registered basic conversion.
            void addParametersToCqlPrimitivesConverters(IEnumerable<Type> tos)
            {
                foreach (Type t in tos) converter.AddConversion(typeof(M.Parameters.ParameterComponent), t,
                    f => converter.ConvertHelper(((M.Parameters.ParameterComponent)f).Value, t)!);
            }
        }

        internal static TypeConverter ConvertCqlPrimitivesToFhir(this TypeConverter converter)
        {
            converter.AddConversion((CqlDate f) => new M.Date(f.ToString()));
            converter.AddConversion((CqlDateTime f) => new M.FhirDateTime(f.ToString()));
            converter.AddConversion((CqlDate f) => new M.FhirDateTime(f.ToString()));
            converter.AddConversion((CqlTime f) => new M.Time(f.ToString()));
            converter.AddConversion((CqlQuantity f) => f.value is not null ? new M.Quantity(f.value.Value, f.unit ?? "1", Hl7.Fhir.ElementModel.Types.Quantity.UCUM) : null);
            converter.AddConversion((CqlInterval<CqlQuantity>? interval) =>
            {
                if (interval is null)
                    return null;
                else
                {
                    var range = new M.Range();
                    if (interval.low is not null && interval.low.value.HasValue)
                    {
                        range.Low = new M.Quantity(interval.low.value.Value, interval.low.unit ?? "1");
                    }
                    if (interval.high is not null && interval.high.value.HasValue)
                    {
                        range.High = new M.Quantity(interval.high.value.Value, interval.high.unit ?? "1");

                    }
                    return range;
                }
            });
            converter.AddConversion((CqlInterval<decimal?> interval) =>
            {
                if (interval is null)
                    return null;
                else
                {
                    var range = new M.Range();
                    if (interval.low is not null && interval.low.HasValue)
                    {
                        range.Low = new M.Quantity(interval.low.Value, "1");
                    }
                    if (interval.high is not null && interval.high.HasValue)
                    {
                        range.High = new M.Quantity(interval.high.Value, "1");
                    }
                    return range;
                }
            });
            converter.AddConversion((CqlInterval<int?> interval) =>
            {
                if (interval is null)
                    return null;
                else
                {
                    var range = new M.Range();
                    if (interval.low is not null && interval.low.HasValue)
                    {
                        range.Low = new M.Quantity(interval.low.Value, "1");
                    }
                    if (interval.high is not null && interval.high.HasValue)
                    {
                        range.High = new M.Quantity(interval.high.Value, "1");
                    }
                    return range;
                }
            });
            converter.AddConversion((CqlInterval<CqlDateTime> interval) =>
            {
                if (interval is null)
                    return null;
                else
                {
                    var period = new M.Period();
                    if (interval.low is not null && interval.low is not null)
                    {
                        period.Start = interval.low.ToString();
                    }
                    if (interval.high is not null && interval.high is not null)
                    {
                        period.End = interval.high.ToString();

                    }
                    return period;
                }
            });
            converter.AddConversion((CqlInterval<CqlDate> interval) =>
            {
                if (interval is null)
                    return null;
                else
                {
                    var period = new M.Period();
                    if (interval.low is not null && interval.low is not null)
                    {
                        period.Start = interval.low.ToString();
                    }
                    if (interval.high is not null && interval.high is not null)
                    {
                        period.End = interval.high.ToString();

                    }
                    return period;
                }
            });
            converter.AddConversion((CqlRatio f) => (f.denominator is not null && f.numerator is not null) ?
                new M.Ratio(converter.Convert<M.Quantity>(f.numerator), converter.Convert<M.Quantity>(f.denominator)) : null);

            return converter;
        }


        internal static TypeConverter ConvertSystemTypes(this TypeConverter converter)
        {
            converter.AddConversion<byte[], string>(binary => Encoding.UTF8.GetString(binary));
            converter.AddConversion<DateTimeOffset?, CqlDateTime?>(dto => dto == null ? null : new CqlDateTime(dto.Value, Iso8601.DateTimePrecision.Millisecond));
            // TODO: this is a performance problem
            converter.AddConversion<string, CqlDate?>(str =>
            {
                if (CqlDate.TryParse(str, out var date))
                    return date!;
                else return null;
            });
            converter.AddConversion<string, CqlDateTime?>(str =>
            {
                if (CqlDateTime.TryParse(str, out var dateTime))
                    return dateTime;
                else return null;
            });
            converter.AddConversion<string, CqlTime?>(str =>
            {
                if (CqlTime.TryParse(str, out var time))
                    return time;
                else return null;
            });
            converter.AddConversion<DateTimeOffset, CqlDateTime>(dto => new CqlDateTime(dto, Iso8601.DateTimePrecision.Millisecond));
            converter.AddConversion<string, FhirUri>(str => new FhirUri(str));
            converter.AddConversion<FhirUri, string>(uri => uri.Value);
            //converter.AddConversion<M.Quantity.QuantityComparator, string>(qc => qc switch
            //{
            //    M.Quantity.QuantityComparator.Ad => "=",
            //    M.Quantity.QuantityComparator.LessThan => "<",
            //    M.Quantity.QuantityComparator.LessOrEqual => "<=",
            //    M.Quantity.QuantityComparator.GreaterThan => ">",
            //    M.Quantity.QuantityComparator.GreaterOrEqual => ">=",
            //    _ => throw new ArgumentException(nameof(qc))
            //});


            return converter;
        }

        internal static TypeConverter ConvertCodeTypes(this TypeConverter converter, ModelInspector model)
        {
            var enumTypes = model.EnumMappings
                .Select(map => map.NativeType)
                .Concat(model.ClassMappings
                    .SelectMany(map => map.NativeType
                        .GetNestedTypes()
                        .Where(t => t.GetCustomAttribute<FhirEnumerationAttribute>() != null)))
                .Distinct()
                .ToArray();
            foreach (var enumType in enumTypes)
            {
                addEnumConversion(enumType);
            }

            void addEnumConversion(Type enumType)
            {
                var codeType = typeof(Code<>).MakeGenericType(enumType);
                var nullableEnumType = typeof(Nullable<>).MakeGenericType(enumType);
                converter.AddConversion(codeType, typeof(CqlCode), (code) =>
                {
                    var systemAndCode = (ISystemAndCode)code;
                    return new CqlCode(systemAndCode.Code, systemAndCode.System, null, null);
                });
                converter.AddConversion(codeType, nullableEnumType, (code) => 
                    code.GetType().GetProperty("Value")!.GetValue(code)!);

                converter.AddConversion(codeType, typeof(string), (code) =>
                {
                    var systemAndCode = (ISystemAndCode)code;
                    return systemAndCode.Code;
                });

                converter.AddConversion(nullableEnumType, typeof(string), (@enum) =>
                    Enum.GetName(nullableEnumType, @enum) ?? throw new InvalidOperationException($"Did not find enum member {@enum} on type {nullableEnumType}."));
            }
            return converter;
        }
    }
}
