using System;
using System.Collections.Generic;
using System.Globalization;

namespace wSpot.Services
{
    /// <summary>
    /// Evaluates typed math such as "2+3*4", "(1+2)^3" or "sqrt(16)/2".
    ///
    /// It is a small recursive-descent parser written by hand, instead of calling
    /// something like DataTable.Compute or a script engine, so that only numbers,
    /// operators and a short list of functions can ever be evaluated.
    ///
    ///   expression := term (('+' | '-') term)*
    ///   term       := unary (('*' | '/' | '%') unary)*
    ///   unary      := ('-' | '+') unary | power
    ///   power      := primary (('^' | '**') unary)?        right-associative: 2^3^2 = 2^9
    ///   primary    := number | '(' expression ')' | name | name '(' expression (',' expression)* ')'
    ///
    /// Because unary minus sits above power, -2^2 is -4, as in maths class.
    /// '%' is the remainder, as in C#.
    ///
    /// Input that merely looks like a number or a name ("5", "pi", "7zip", "vs2022")
    /// is not a calculation. A result is only offered when at least one operator or
    /// function call was used, so ordinary searches never grow a calculator row.
    /// </summary>
    public static class ExpressionEvaluator
    {
        private const int MaxLength = 100;

        private static readonly Dictionary<string, double> Constants = new Dictionary<string, double>
        {
            ["pi"] = Math.PI,
            ["e"] = Math.E,
            ["tau"] = Math.Tau
        };

        /// <summary>
        /// True when <paramref name="text"/> is a valid calculation that uses at least
        /// one operator or function, with a finite result.
        /// </summary>
        public static bool TryEvaluate(string text, out double value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
                return false;

            var parser = new Parser(text);

            try
            {
                double result = parser.ParseAll();

                if (!parser.UsedOperator) return false;
                if (double.IsNaN(result) || double.IsInfinity(result)) return false;

                value = result == 0 ? 0 : result; // turns -0 into 0
                return true;
            }
            catch (ExpressionException)
            {
                return false;
            }
        }

        /// <summary>
        /// Up to 15 significant digits, no exponent for ordinary sizes. 15 hides the
        /// floating-point noise in the 16th and 17th digit (0.1+0.2 shows as 0.3).
        /// </summary>
        public static string Format(double value) =>
            (value == 0 ? 0 : value).ToString("G15", CultureInfo.InvariantCulture);

        private sealed class ExpressionException : Exception
        {
            public ExpressionException(string message) : base(message) { }
        }

        private static ExpressionException Fail(string message) => new ExpressionException(message);

        /// <summary>Every intermediate result must be a real number, so 1/(1/0) and sqrt(-1) are rejected.</summary>
        private static double Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw Fail("result is not a finite number");

            return value;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private sealed class Parser
        {
            private readonly string _text;
            private int _position;

            public Parser(string text)
            {
                _text = text;
            }

            /// <summary>Set once an operator or a function call has been seen.</summary>
            public bool UsedOperator { get; private set; }

            public double ParseAll()
            {
                double value = ParseExpression();
                SkipSpaces();

                if (_position != _text.Length)
                    throw Fail("unexpected text after the expression");

                return value;
            }

            private double ParseExpression()
            {
                double value = ParseTerm();

                while (true)
                {
                    SkipSpaces();
                    char c = Peek();

                    if (c == '+')
                    {
                        _position++;
                        UsedOperator = true;
                        value = Finite(value + ParseTerm());
                    }
                    else if (c == '-')
                    {
                        _position++;
                        UsedOperator = true;
                        value = Finite(value - ParseTerm());
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double ParseTerm()
            {
                double value = ParseUnary();

                while (true)
                {
                    SkipSpaces();
                    char c = Peek();

                    if (c == '*')
                    {
                        _position++;
                        UsedOperator = true;
                        value = Finite(value * ParseUnary());
                    }
                    else if (c == '/')
                    {
                        _position++;
                        UsedOperator = true;
                        double divisor = ParseUnary();
                        if (divisor == 0) throw Fail("division by zero");
                        value = Finite(value / divisor);
                    }
                    else if (c == '%')
                    {
                        _position++;
                        UsedOperator = true;
                        double divisor = ParseUnary();
                        if (divisor == 0) throw Fail("remainder by zero");
                        value = Finite(value % divisor);
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double ParseUnary()
            {
                SkipSpaces();
                char c = Peek();

                if (c == '-')
                {
                    _position++;
                    return -ParseUnary();
                }

                if (c == '+')
                {
                    _position++;
                    return ParseUnary();
                }

                return ParsePower();
            }

            private double ParsePower()
            {
                double baseValue = ParsePrimary();
                SkipSpaces();

                if (Peek() == '^')
                    _position++;
                else if (Peek() == '*' && Peek(1) == '*')
                    _position += 2;
                else
                    return baseValue;

                UsedOperator = true;

                // The exponent is a full unary, so 2^-1 works and 2^3^2 groups to the right.
                double exponent = ParseUnary();
                return Finite(Math.Pow(baseValue, exponent));
            }

            private double ParsePrimary()
            {
                SkipSpaces();
                char c = Peek();

                if (c == '(')
                {
                    _position++;
                    double inner = ParseExpression();
                    SkipSpaces();

                    if (Peek() != ')') throw Fail("')' expected");
                    _position++;
                    return inner;
                }

                if (IsDigit(c) || c == '.') return ParseNumber();
                if (IsLetter(c) || c == '_') return ParseName();

                throw Fail("unexpected character");
            }

            private double ParseNumber()
            {
                int start = _position;
                bool sawDigit = false;

                while (IsDigit(Peek()))
                {
                    _position++;
                    sawDigit = true;
                }

                if (Peek() == '.')
                {
                    _position++;

                    while (IsDigit(Peek()))
                    {
                        _position++;
                        sawDigit = true;
                    }
                }

                if (!sawDigit) throw Fail("number expected");

                // An exponent like 1e3 only counts when digits follow it, so "2e" stays an
                // error instead of silently ending the number.
                if (Peek() == 'e' || Peek() == 'E')
                {
                    int look = _position + 1;

                    if (look < _text.Length && (_text[look] == '+' || _text[look] == '-'))
                        look++;

                    if (look < _text.Length && IsDigit(_text[look]))
                    {
                        _position = look;

                        while (IsDigit(Peek()))
                            _position++;
                    }
                }

                string slice = _text.Substring(start, _position - start);

                if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    throw Fail("not a number");

                if (double.IsInfinity(number)) throw Fail("number too large");

                return number;
            }

            private double ParseName()
            {
                int start = _position;

                while (IsLetter(Peek()) || IsDigit(Peek()) || Peek() == '_')
                    _position++;

                string name = _text.Substring(start, _position - start).ToLowerInvariant();

                SkipSpaces();

                if (Peek() == '(')
                {
                    _position++;

                    var arguments = new List<double> { ParseExpression() };
                    SkipSpaces();

                    while (Peek() == ',')
                    {
                        _position++;
                        arguments.Add(ParseExpression());
                        SkipSpaces();
                    }

                    if (Peek() != ')') throw Fail("')' expected");
                    _position++;

                    UsedOperator = true; // a function call is real maths, like an operator
                    return CallFunction(name, arguments);
                }

                if (Constants.TryGetValue(name, out double constant))
                    return constant;

                throw Fail("unknown name: " + name);
            }

            private static double CallFunction(string name, List<double> arguments)
            {
                if (arguments.Count == 1)
                {
                    double x = arguments[0];

                    switch (name)
                    {
                        case "sqrt": return Finite(Math.Sqrt(x));
                        case "cbrt": return Finite(Math.Cbrt(x));
                        case "abs": return Finite(Math.Abs(x));
                        case "sin": return SnapTrig(x, Finite(Math.Sin(x)));
                        case "cos": return SnapTrig(x, Finite(Math.Cos(x)));
                        case "tan": return SnapTrig(x, Finite(Math.Tan(x)));
                        case "asin": return Finite(Math.Asin(x));
                        case "acos": return Finite(Math.Acos(x));
                        case "atan": return Finite(Math.Atan(x));
                        case "ln": return Finite(Math.Log(x));
                        case "log": return Finite(Math.Log10(x));
                        case "log2": return Finite(Math.Log2(x));
                        case "exp": return Finite(Math.Exp(x));
                        case "floor": return Finite(Math.Floor(x));
                        case "ceil": return Finite(Math.Ceiling(x));
                        case "round": return Finite(Math.Round(x, MidpointRounding.AwayFromZero)); // 2.5 -> 3, as people expect
                    }
                }
                else if (arguments.Count == 2)
                {
                    switch (name)
                    {
                        case "pow": return Finite(Math.Pow(arguments[0], arguments[1]));
                        case "min": return Math.Min(arguments[0], arguments[1]);
                        case "max": return Math.Max(arguments[0], arguments[1]);
                    }
                }

                throw Fail("unknown function or wrong number of arguments: " + name);
            }

            /// <summary>
            /// In floating point, sin(pi) is 1.2e-16 rather than 0. For an ordinary-sized
            /// angle, a result that tiny is rounding noise, so show it as 0.
            /// </summary>
            private static double SnapTrig(double argument, double result) =>
                Math.Abs(argument) > 1e-6 && Math.Abs(result) < 1e-15 ? 0.0 : result;

            private char Peek(int offset = 0)
            {
                int index = _position + offset;
                return index < _text.Length ? _text[index] : '\0';
            }

            private void SkipSpaces()
            {
                while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                    _position++;
            }
        }
    }
}
