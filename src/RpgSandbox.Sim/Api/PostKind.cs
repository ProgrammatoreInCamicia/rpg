namespace RpgSandbox.Sim.Api;

/// <summary>
/// Named squares of a place on a map (T6c): where routines take people. A post is a destination, never a reach or a
/// sight: being on the "Guard" post covers a store only if the rules of sight and reach say so.
/// </summary>
public enum PostKind { Work, Home, Guard, Rest }
