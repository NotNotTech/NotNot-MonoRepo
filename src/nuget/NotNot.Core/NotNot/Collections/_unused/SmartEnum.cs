using System.Runtime.CompilerServices;

namespace NotNot.Collections._unused;

/// <summary>
///    basic implementation of a smart enum, fixing the super dumb parts of
///    https://github.com/ardalis/SmartEnum/blob/main/src/SmartEnum/SmartEnum.cs
///    such as needing a custom class for each enum value.
///    need to fill in various enum helper functions as needed.
/// </summary>
/// <typeparam name="TEnum"></typeparam>
public abstract class SmartEnum<TEnum> where TEnum : SmartEnum<TEnum>
{
   protected SmartEnum([CallerMemberName] string name = "")
   {
      Name = name;
   }

   public string Name { get; init; }

   public override string ToString()
   {
      return Name;
   }

   //public static implicit operator int(SmartEnum<TEnum> smartEnum) => smartEnum.value;
   public static implicit operator string(SmartEnum<TEnum> smartEnum)
   {
      return smartEnum.Name;
   }

   public static bool operator ==(SmartEnum<TEnum> a, SmartEnum<TEnum> b)
   {
      if (a is null)
      {
         return false;
      }

      return a.Equals(b);
   }

   public static bool operator !=(SmartEnum<TEnum> a, SmartEnum<TEnum> b)
   {
      return !(a == b);
   }


   public override bool Equals(object? obj)
   {
      if (ReferenceEquals(this, obj))
      {
         return true;
      }

      if (obj is null)
      {
         return false;
      }

      if (GetType() != obj.GetType())
      {
         return false;
      }

      var b = obj as TEnum;

      return Name == b.Name;
   }

   public override int GetHashCode()
   {
      return Name.GetHashCode();
   }
}