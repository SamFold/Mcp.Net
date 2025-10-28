using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mcp.Net.Core.Attributes;

namespace Mcp.Net.Examples.SimpleServer
{
    /// <summary>
    /// Provides basic calculator and geometry functions as MCP tools
    /// </summary>
    [McpTool(
        "calculator",
        "Calculator and geometry tools for solving mathematical problems",
        Category = "math",
        CategoryDisplayName = "Math & Geometry"
    )]
    public class CalculatorTools
    {
        [McpTool(
            "calculator_add",
            "Add two numbers together",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Add(
            [McpParameter(required: true, description: "First number")] double a,
            [McpParameter(required: true, description: "Second number")] double b
        )
        {
            return a + b;
        }

        [McpTool(
            "calculator_subtract",
            "Subtract one number from another",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Subtract(
            [McpParameter(required: true, description: "Number to subtract from")] double a,
            [McpParameter(required: true, description: "Number to subtract")] double b
        )
        {
            return a - b;
        }

        [McpTool(
            "calculator_multiply",
            "Multiply two numbers",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Multiply(
            [McpParameter(required: true, description: "First number")] double a,
            [McpParameter(required: true, description: "Second number")] double b
        )
        {
            return a * b;
        }

        [McpTool(
            "calculator_divide",
            "Divide one number by another",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static CalculationResult Divide(
            [McpParameter(required: true, description: "Dividend (number to be divided)")] double a,
            [McpParameter(required: true, description: "Divisor (number to divide by)")] double b
        )
        {
            if (b == 0)
            {
                return new CalculationResult
                {
                    Success = false,
                    ErrorMessage = "Cannot divide by zero",
                    Result = 0, // Use 0 instead of NaN which can't be serialized to JSON
                };
            }

            return new CalculationResult
            {
                Success = true,
                Result = a / b,
                Operation = $"{a} ÷ {b}",
            };
        }

        [McpTool(
            "calculator_power",
            "Raise a number to a power",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Power(
            [McpParameter(required: true, description: "Base number")] double baseNumber,
            [McpParameter(required: true, description: "Exponent")] double exponent
        )
        {
            return Math.Pow(baseNumber, exponent);
        }

        [McpTool(
            "calculator_sin",
            "Calculate the sine of an angle in degrees",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Sin(
            [McpParameter(required: true, description: "Angle in degrees")] double angleDegrees
        )
        {
            double angleRadians = angleDegrees * Math.PI / 180.0;
            return Math.Sin(angleRadians);
        }

        [McpTool(
            "calculator_cos",
            "Calculate the cosine of an angle in degrees",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Cos(
            [McpParameter(required: true, description: "Angle in degrees")] double angleDegrees
        )
        {
            double angleRadians = angleDegrees * Math.PI / 180.0;
            return Math.Cos(angleRadians);
        }

        [McpTool(
            "calculator_tan",
            "Calculate the tangent of an angle in degrees",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static CalculationResult Tan(
            [McpParameter(required: true, description: "Angle in degrees")] double angleDegrees
        )
        {
            // Check for angles where tangent is undefined (90°, 270°, etc.)
            if (Math.Abs(angleDegrees % 180) == 90)
            {
                return new CalculationResult
                {
                    Success = false,
                    ErrorMessage = $"Tangent is undefined at {angleDegrees}°",
                    Result = 0,
                };
            }

            double angleRadians = angleDegrees * Math.PI / 180.0;
            return new CalculationResult
            {
                Success = true,
                Result = Math.Tan(angleRadians),
                Operation = $"tan({angleDegrees}°)",
            };
        }

        [McpTool(
            "calculator_asin",
            "Calculate the arcsine (inverse sine) in degrees",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static CalculationResult Asin(
            [McpParameter(required: true, description: "Value between -1 and 1")] double value
        )
        {
            if (value < -1 || value > 1)
            {
                return new CalculationResult
                {
                    Success = false,
                    ErrorMessage = "Arcsine input must be between -1 and 1",
                    Result = 0,
                };
            }

            double angleRadians = Math.Asin(value);
            double angleDegrees = angleRadians * 180.0 / Math.PI;

            return new CalculationResult
            {
                Success = true,
                Result = angleDegrees,
                Operation = $"asin({value})",
            };
        }

        [McpTool(
            "calculator_acos",
            "Calculate the arccosine (inverse cosine) in degrees",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static CalculationResult Acos(
            [McpParameter(required: true, description: "Value between -1 and 1")] double value
        )
        {
            if (value < -1 || value > 1)
            {
                return new CalculationResult
                {
                    Success = false,
                    ErrorMessage = "Arccosine input must be between -1 and 1",
                    Result = 0,
                };
            }

            double angleRadians = Math.Acos(value);
            double angleDegrees = angleRadians * 180.0 / Math.PI;

            return new CalculationResult
            {
                Success = true,
                Result = angleDegrees,
                Operation = $"acos({value})",
            };
        }

        [McpTool(
            "calculator_atan",
            "Calculate the arctangent (inverse tangent) in degrees",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Atan(
            [McpParameter(required: true, description: "Value to find the arctangent of")]
                double value
        )
        {
            double angleRadians = Math.Atan(value);
            double angleDegrees = angleRadians * 180.0 / Math.PI;
            return angleDegrees;
        }

        [McpTool(
            "calculator_atan2",
            "Calculate the angle (in degrees) from the X-axis to a point",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Atan2(
            [McpParameter(required: true, description: "Y coordinate")] double y,
            [McpParameter(required: true, description: "X coordinate")] double x
        )
        {
            double angleRadians = Math.Atan2(y, x);
            double angleDegrees = angleRadians * 180.0 / Math.PI;
            return angleDegrees;
        }

        [McpTool(
            "calculator_distance",
            "Calculate the distance between two points (2D)",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Distance(
            [McpParameter(required: true, description: "X coordinate of first point")] double x1,
            [McpParameter(required: true, description: "Y coordinate of first point")] double y1,
            [McpParameter(required: true, description: "X coordinate of second point")] double x2,
            [McpParameter(required: true, description: "Y coordinate of second point")] double y2
        )
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        [McpTool(
            "calculator_circle_area",
            "Calculate the area of a circle",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double CircleArea(
            [McpParameter(required: true, description: "Radius of the circle")] double radius
        )
        {
            return Math.PI * radius * radius;
        }

        [McpTool(
            "calculator_circle_circumference",
            "Calculate the circumference of a circle",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double CircleCircumference(
            [McpParameter(required: true, description: "Radius of the circle")] double radius
        )
        {
            return 2 * Math.PI * radius;
        }

        [McpTool(
            "calculator_triangle_area",
            "Calculate the area of a triangle",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double TriangleArea(
            [McpParameter(required: true, description: "Length of side a")] double a,
            [McpParameter(required: true, description: "Length of side b")] double b,
            [McpParameter(required: true, description: "Length of side c")] double c
        )
        {
            // Heron's formula
            double s = (a + b + c) / 2; // Semi-perimeter
            double area = Math.Sqrt(s * (s - a) * (s - b) * (s - c));
            return area;
        }

        [McpTool(
            "calculator_triangle_area_base_height",
            "Calculate the area of a triangle using base and height",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double TriangleAreaBaseHeight(
            [McpParameter(required: true, description: "Length of the base")] double baseLength,
            [McpParameter(required: true, description: "Height of the triangle")] double height
        )
        {
            return 0.5 * baseLength * height;
        }

        [McpTool(
            "calculator_pythagorean",
            "Calculate the hypotenuse of a right triangle",
            Category = "math",
            CategoryDisplayName = "Math & Geometry"
        )]
        public static double Pythagorean(
            [McpParameter(required: true, description: "Length of side a")] double a,
            [McpParameter(required: true, description: "Length of side b")] double b
        )
        {
            return Math.Sqrt(a * a + b * b);
        }
    }

    public class CalculationResult
    {
        public bool Success { get; set; } = true;
        public double Result { get; set; }
        public string? Operation { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
