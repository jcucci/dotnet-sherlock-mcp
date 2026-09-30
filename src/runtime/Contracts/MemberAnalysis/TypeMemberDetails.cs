namespace Sherlock.MCP.Runtime.Contracts.MemberAnalysis;

public record TypeMemberDetails(
    MemberKind Kind,
    string Name,
    string Signature,
    MethodDetails? Method = null,
    PropertyDetails? Property = null,
    FieldDetails? Field = null,
    EventDetails? Event = null,
    ConstructorDetails? Constructor = null
)
{
    public static TypeMemberDetails FromMethod(MethodDetails method) =>
        new(MemberKind.Method, method.Name, method.Signature, Method: method);

    public static TypeMemberDetails FromProperty(PropertyDetails property) =>
        new(MemberKind.Property, property.Name, property.Signature, Property: property);

    public static TypeMemberDetails FromField(FieldDetails field) =>
        new(MemberKind.Field, field.Name, field.Signature, Field: field);

    public static TypeMemberDetails FromEvent(EventDetails eventDetails) =>
        new(MemberKind.Event, eventDetails.Name, eventDetails.Signature, Event: eventDetails);

    public static TypeMemberDetails FromConstructor(ConstructorDetails constructor) =>
        new(MemberKind.Constructor, constructor.IsStatic ? ".cctor" : ".ctor", constructor.Signature, Constructor: constructor);
}
