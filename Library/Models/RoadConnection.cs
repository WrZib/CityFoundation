using System;

namespace Library.Models;

[Flags] //комбинирование побитовом или
//в одном байте хранится 4 направления
public enum RoadConnection : byte
{
    None = 0,
    North = 1,
    East = 2,
    South = 4,
    West = 8,
    All = North | East | South | West
}