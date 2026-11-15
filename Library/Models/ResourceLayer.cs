using System;

namespace Library.Models;

[Flags]
public enum ResourceLayer
{
    None = 0,
    Water = 1,
    Stone = 2,
    Iron = 4,
    Copper = 8,
    Oil = 16,
    Gas = 32,
    All = Water | Stone | Iron | Copper | Oil | Gas
}
