using Microsoft.IdentityModel.Tokens;

namespace Onyx.App.Web.Services.Auth;

public class KeyAccessor(RsaSecurityKey applicationKey)
{
    public RsaSecurityKey ApplicationKey => applicationKey;
}