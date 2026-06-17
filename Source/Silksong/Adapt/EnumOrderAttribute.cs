using System;

// TeamCherry's [EnumOrder(n)] attribute decorates enum members in the extracted GlobalEnums.* enums. HK lacks it.
// Global namespace so it's visible from the decompiled enum files (which sit in `namespace GlobalEnums`). Inert.
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public sealed class EnumOrderAttribute : Attribute {
    public EnumOrderAttribute(int order) { }
}
