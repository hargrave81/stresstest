using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace StressTest
{
    public class Token
    {
        /// <summary>
        /// used for all token calls
        /// </summary>
        private static HttpClient client;

        /// <summary>
        /// lock async objects
        /// </summary>
        private static readonly SemaphoreSlim clientLock = new SemaphoreSlim(1);

        #region public properties
        /// <summary>
        /// Access token used for API Calls
        /// </summary>
        public string AccessToken { get; set; }
        /// <summary>
        /// UTC Expiration
        /// </summary>
        public DateTime Expires { get; set; }
        /// <summary>
        /// Groups/Roles the user is associated with [if this is a user token]
        /// </summary>
        public string[] Groups { get; set; }
        /// <summary>
        /// Email [if this is a user token]
        /// </summary>
        public string Email { get; set; }
        /// <summary>
        /// ID [if this is a user token]
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// Scopes
        /// </summary>
        public string[] Scopes { get; set; }
        /// <summary>
        /// Client ID of Token [if generated from a get token request]
        /// </summary>
        public string ClientId { get; set; }
        /// <summary>
        /// User Name [if this is a user token]
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Error message if a failed to process occurs
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Returns true if the total time remaining on the token is greater than 15 seconds
        /// </summary>
        public bool StillValid
        {
            get
            {
                return DateTime.UtcNow.Subtract(Expires).TotalSeconds <= -15;
            }
        }

        #endregion

        #region static routines

        /// <summary>
        /// Parses a JWT Token
        /// </summary>
        /// <param name="base64Token"></param>
        /// <returns></returns>
        public static Token ParseToken(string base64Token)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(base64Token);
            var result = new Token();
            result.Scopes = token.Claims.Where(t => t.Type.ToLower() == "scope").Select(t => t.Value).ToArray();
            result.Id = token.Subject;
            result.Expires = token.ValidTo;
            result.Groups = token.Claims.Where(t => t.Type.ToLower() == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role").Select(t => t.Value).ToArray();
            result.Email = token.Claims.Any(t => t.Type.ToLower() == "email")
                ? token.Claims.First(t => t.Type.ToLower() == "email").Value
                : "";
            result.Email = token.Claims.Any(t => t.Type.ToLower() == "name")
                ? token.Claims.First(t => t.Type.ToLower() == "name").Value
                : "";
            result.ClientId = token.Claims.Any(t => t.Type.ToLower() == "client_id")
                ? token.Claims.First(t => t.Type.ToLower() == "client_id").Value
                : "";
            result.AccessToken = base64Token;
            return result;
        }

        public static async Task<Token> GetTokenAsync(string idpUri, string clientId, string secret, string[] scopes, CancellationToken cancelToken)
        {
            await clientLock.WaitAsync(cancelToken);

            if (cancelToken.IsCancellationRequested) // abort this request
                return null;

            if (client == null)
            {
                client = new HttpClient();
            }
            var kvp = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            };
            foreach (var scope in scopes)
            {
                kvp.Add(new KeyValuePair<string, string>("scope", scope));
            }
            // ensure the url provided is the token endpoint
            if (!idpUri.Contains("/token"))
            {
                if (!idpUri.EndsWith("/"))
                {
                    idpUri += "/";
                }

                idpUri += "connect/token";
            }

            try
            {
                var message = new HttpRequestMessage(HttpMethod.Post, idpUri);
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}")));
                // add accept JSON
                message.Headers.Add("Accept", "application/json");
                message.Content = new FormUrlEncodedContent(kvp);
                var result = await client.SendAsync(message, cancelToken);
                if ((int)result.StatusCode == 200)
                {
                    var accessTokenResult = result.ReadResultAs<AuthResponseSuccess>();
                    var tokenResult = ParseToken(accessTokenResult.access_token);
                    tokenResult.ClientId = clientId;
                    clientLock.Release();
                    return tokenResult;
                }

                var subResult = result.ReadResultAs<AuthResponseFailed>();

                if (string.IsNullOrEmpty(subResult.Error_Description) &&
                    subResult.Error == AuthError.InvalidClient.ToString())
                {
                    // grab the error from the header
                    subResult.Error = AuthError.AuthenticationFailed.ToString();
                    subResult.Error_Description =
                        string.Join(" ", result.Headers.First(t => t.Key == "WWW-Authenticate").Value);
                }
                clientLock.Release();
                return new Token() { ErrorMessage = subResult.Error_Description, ClientId = clientId };
            }
            catch (Exception ex)
            {
                clientLock.Release();
                return new Token() { ErrorMessage = ex.Message, ClientId = clientId };
            }
        }


        public static Token GetToken(string idpUri, string clientId, string secret, string[] scopes)
        {
            return AsyncHelpers.RunSync(() => GetTokenAsync(idpUri, clientId, secret, scopes, new CancellationToken()));
        }
        #endregion
    }

    /// <summary>
    /// Response from the authentication system
    /// </summary>
    public class AuthResponse
    {
        /// <summary>
        /// True or false if the call was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Returns this object as a successful response
        /// </summary>
        [JsonIgnore]
        public AuthResponseSuccess AsSuccess => (AuthResponseSuccess)this;

        /// <summary>
        /// Returns this object as a failed response
        /// </summary>
        [JsonIgnore]
        public AuthResponseFailed AsFailed => (AuthResponseFailed)this;
    }

    /// <summary>
    /// Failed auth response
    /// </summary>
    public class AuthResponseFailed : AuthResponse
    {
        /// <summary>
        /// Error Type
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Error Description
        /// </summary>
        public string Error_Description { get; set; }

        #region cTor
        /// <summary>
        /// Creates default instance
        /// </summary>
        public AuthResponseFailed()
        {
            Success = false;
        }

        /// <summary>
        /// Creates instance with values
        /// </summary>
        /// <param name="error"></param>
        /// <param name="message"></param>
        public AuthResponseFailed(AuthError error, string message) : this()
        {
            Error = error.ToString();
            Error_Description = message;
        }
        #endregion
    }

    /// <summary>
    /// Successful auth response
    /// </summary>
    public class AuthResponseSuccess : AuthResponse
    {
        /// <summary>
        /// Access token used to interact with API, submitted in the header of requests
        /// </summary>
        public string access_token { get; set; }

        /// <summary>
        /// Type of token returned
        /// </summary>
        public string token_type { get; set; }

        /// <summary>
        /// Time frame token remains good for
        /// </summary>
        public int expires_in { get; set; }

        /// <summary>
        /// Scope this token is good for
        /// </summary>
        public string scope { get; set; }

        #region cTor
        /// <summary>
        /// Creates default instance
        /// </summary>
        public AuthResponseSuccess()
        {
            Success = true;
        }

        /// <summary>
        /// Creates instance with values
        /// </summary>
        /// <param name="accessToken"></param>
        /// <param name="tokenType"></param>
        /// <param name="expiresIn"></param>
        /// <param name="scope"></param>
        public AuthResponseSuccess(string accessToken, string tokenType, int expiresIn, string scope) : this()
        {
            access_token = accessToken;
            token_type = tokenType;
            expires_in = expiresIn;
            this.scope = scope;
        }
        #endregion
    }

    /// <summary>
    /// Authentication errors
    /// </summary>
    [Serializable]
    [Flags]
    public enum AuthError
    {
        /// <summary>
        /// Invalid Client
        /// </summary>
        InvalidClient,
        /// <summary>
        /// Invalid Scope
        /// </summary>
        InvalidScope,
        /// <summary>
        /// Invalid Grant Type
        /// </summary>
        InvalidGrantType,
        /// <summary>
        /// Grant Type was not allowed
        /// </summary>
        GrantTypeNotAllowed,
        /// <summary>
        /// Authentication Failed
        /// </summary>
        AuthenticationFailed
    }

    /// <summary>
    /// HTTP Response Reader helper class - uses Newtonsoft unless running .NET Core 3.x
    /// Use the JsonSerializer to specify newtonsoft always
    /// </summary>
    public static class HttpResonseReader
    {
        /// <summary>
        /// Reads a response into a list of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <returns></returns>
        async public static Task<IEnumerable<T>> ReadResultAsListAsync<T>(this HttpResponseMessage message)
        {
            if (message.StatusCode != System.Net.HttpStatusCode.OK)
                return new List<T>();
            string msg = await message.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(msg))
                return new List<T>();
            if (message.Content.Headers.ContentType.MediaType.StartsWith("application/json") || msg.StartsWith("{"))
            {
                // json
                var results = JsonSerializer.Deserialize<List<T>>(msg);
                return results;
            }
            else
            {
                // xml
                XmlSerializer x = new XmlSerializer(typeof(List<T>));
                var results = (IEnumerable<T>)x.Deserialize(new StringReader(msg));
                return results;
            }
        }

        /// <summary>
        /// Reads a response into a list of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <returns></returns>
        public static IEnumerable<T> ReadResultAsList<T>(this HttpResponseMessage message)
        {
            return AsyncHelpers.RunSync(() => ReadResultAsListAsync<T>(message));
        }

        /// <summary>
        /// reads a response into type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <returns></returns>
        async public static Task<T> ReadResultAsAsync<T>(this HttpResponseMessage message)
        {
            string msg = await message.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(msg))
                return default(T);
            if (message.Content.Headers.ContentType.MediaType.StartsWith("application/json") || msg.StartsWith("{"))
            {
                // json
                var results = JsonSerializer.Deserialize<T>(msg);
                return results;
            }
            else
            {
                // xml
                XmlSerializer x = new XmlSerializer(typeof(T));
                var results = (T)x.Deserialize(new StringReader(msg));
                return results;
            }
        }

        /// <summary>
        /// reads a response into type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message"></param>
        /// <returns></returns>
        public static T ReadResultAs<T>(this HttpResponseMessage message)
        {
            return AsyncHelpers.RunSync(() => ReadResultAsAsync<T>(message));
        }
    }

    /// <summary>
    /// Json Serializer settings to dynamically support a newtonsoft-free runtime
    /// </summary>
    public static class JsonSerializer
    {
        private static bool alwaysUseNewtonsoftJson = false;

        /// <summary>
        /// Set the value to always use newtonsoft JSON to parse when using the LKQ Web Extensions parsing
        /// </summary>
        public static bool AlwaysUseNewtonsoftJson
        {
            get { return alwaysUseNewtonsoftJson; }
            set { alwaysUseNewtonsoftJson = value; }
        }

        /// <summary>
        /// Deserializes an object based on the preferred platforms serializer
        /// </summary>
        /// <param name="jsonString"></param>
        /// <returns></returns>
        public static T Deserialize<T>(string jsonString)
        {
            
                return System.Text.Json.JsonSerializer.Deserialize<T>(jsonString);
            
        }


        /// <summary>
        /// Serializes an object with the preferred platforms serializer
        /// </summary>
        /// <param name="serializable"></param>
        /// <returns></returns>
        public static string Serialize(object serializable)
        {
            
                return System.Text.Json.JsonSerializer.Serialize(serializable);
            
        }
    }

}
