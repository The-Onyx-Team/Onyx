using Microsoft.IdentityModel.Tokens;

namespace Onyx.App.Web.Services.Auth;

public class KeyAccessor(RsaSecurityKey applicationKey)
{
    public virtual RsaSecurityKey ApplicationKey => applicationKey;
}