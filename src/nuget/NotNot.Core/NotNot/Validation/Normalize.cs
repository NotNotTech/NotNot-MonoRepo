using NotNot.Validation._internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NotNot.Validation;
public class Normalize
{
   public MailAddress CanonicalEmail(string email)
   {
      var parts = email.Trim().ToLower().Split('@');
      if (parts.Length != 2)
      {
         throw new ArgumentException("invalid email address", nameof(email));
      }
      email = parts[0] + "@" + parts[1];

      var result = MailAddress.TryCreate(email, out var asMailAddress);
      if (!result)
      {
         throw new ArgumentException("invalid email address", nameof(email));
      }
      return EmailNormalizer.Normalize(asMailAddress);
   }
}
