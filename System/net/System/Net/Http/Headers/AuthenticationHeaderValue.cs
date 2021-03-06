﻿using System.Diagnostics.Contracts;

namespace System.Net.Http.Headers
{
    public class AuthenticationHeaderValue : ICloneable
    {
        private string scheme;
        private string parameter;

        public string Scheme
        {
            get { return scheme; }
        }

        // We simplify parameters by just considering them one string. The caller is responsible for correctly parsing
        // the string.
        // The reason is that we can't determine the format of parameters. According to Errata 1959 in RFC 2617 
        // parameters can be "token", "quoted-string", or "#auth-param" where "auth-param" is defined as 
        // "token "=" ( token | quoted-string )". E.g. take the following BASIC example:
        // Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==
        // Due to Base64 encoding we have two final "=". The value is neither a token nor a quoted-string, so it must
        // be an auth-param according to the RFC definition. But that's also incorrect: auth-param means that we 
        // consider the value before the first "=" as "name" and the final "=" as "value". 
        public string Parameter
        {
            get { return parameter; }
        }

        public AuthenticationHeaderValue(string scheme)
            : this(scheme, null)
        {
        }

        public AuthenticationHeaderValue(string scheme, string parameter)
        {
            HeaderUtilities.CheckValidToken(scheme, "scheme");
            this.scheme = scheme;
            this.parameter = parameter;
        }

        private AuthenticationHeaderValue(AuthenticationHeaderValue source)
        {
            Contract.Requires(source != null);

            this.scheme = source.scheme;
            this.parameter = source.parameter;
        }

        private AuthenticationHeaderValue()
        {
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(parameter))
            {
                return scheme;
            }
            return scheme + " " + parameter;
        }

        public override bool Equals(object obj)
        {
            AuthenticationHeaderValue other = obj as AuthenticationHeaderValue;

            if (other == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(parameter) && string.IsNullOrEmpty(other.parameter))
            {
                return (string.Compare(scheme, other.scheme, StringComparison.OrdinalIgnoreCase) == 0);
            }
            else
            {
                // Since we can't parse the parameter, we use case-sensitive comparison.
                return (string.Compare(scheme, other.scheme, StringComparison.OrdinalIgnoreCase) == 0) &&
                    (string.CompareOrdinal(parameter, other.parameter) == 0);
            }
        }

        public override int GetHashCode()
        {
            int result = scheme.ToLowerInvariant().GetHashCode();

            if (!string.IsNullOrEmpty(parameter))
            {
                result = result ^ parameter.GetHashCode();
            }

            return result;
        }

        public static AuthenticationHeaderValue Parse(string input)
        {            
            int index = 0;
            return (AuthenticationHeaderValue)GenericHeaderParser.SingleValueAuthenticationParser.ParseValue(
                input, null, ref index);
        }

        public static bool TryParse(string input, out AuthenticationHeaderValue parsedValue)
        {
            int index = 0;
            object output;
            parsedValue = null;

            if (GenericHeaderParser.SingleValueAuthenticationParser.TryParseValue(input, null, ref index, out output))
            {
                parsedValue = (AuthenticationHeaderValue)output;
                return true;
            }
            return false;
        }

        internal static int GetAuthenticationLength(string input, int startIndex, out object parsedValue)
        {
            Contract.Requires(startIndex >= 0);

            parsedValue = null;

            if (string.IsNullOrEmpty(input) || (startIndex >= input.Length))
            {
                return 0;
            }

            // Parse the scheme string: <scheme> in '<scheme> <parameter>'
            int schemeLength = HttpRuleParser.GetTokenLength(input, startIndex);

            if (schemeLength == 0)
            {
                return 0;
            }

            AuthenticationHeaderValue result = new AuthenticationHeaderValue();
            result.scheme = input.Substring(startIndex, schemeLength);

            int current = startIndex + schemeLength;
            int whitespaceLength = HttpRuleParser.GetWhitespaceLength(input, current);
            current = current + whitespaceLength;

            if ((current == input.Length) || (input[current] == ','))
            {
                // If we only have a scheme followed by whitespaces, we're done.
                parsedValue = result;
                return current - startIndex;
            }

            // We need at least one space between the scheme and parameters. If there are no whitespaces, then we must
            // have reached the end of the string (i.e. scheme-only string).
            if (whitespaceLength == 0)
            {
                return 0;
            }

            // If we get here, we have a <scheme> followed by a whitespace. Now we expect the following:
            // '<scheme> <blob>[,<name>=<value>]*[, <otherscheme>...]*': <blob> potentially contains one 
            // or more '=' characters, optionally followed by additional name/value pairs, optionally followed by 
            // other schemes. <blob> may be a quoted string.
            // We look at the value after ',': if it is <token>=<value> then we have a parameter for <scheme>.
            // If we have either a <token>-only or <token><whitespace><blob> then we have another scheme.
            int parameterStartIndex = current;
            int parameterEndIndex = current;
            if (!TrySkipFirstBlob(input, ref current, ref parameterEndIndex))
            {
                return 0;
            }

            if (current < input.Length)
            {
                if (!TryGetParametersEndIndex(input, ref current, ref parameterEndIndex))
                {
                    return 0;
                }
            }

            result.parameter = input.Substring(parameterStartIndex, parameterEndIndex - parameterStartIndex + 1);
            parsedValue = result;
            return current - startIndex;
        }

        private static bool TrySkipFirstBlob(string input, ref int current, ref int parameterEndIndex)
        {
            // Find the delimiter: Note that <blob> in "<scheme> <blob>" may be a token, quoted string, name/value
            // pair or a Base64 encoded string. So make sure that we don't consider ',' characters within a quoted
            // string as delimiter.
            while ((current < input.Length) && (input[current] != ','))
            {
                if (input[current] == '"')
                {
                    int quotedStringLength = 0;
                    if (HttpRuleParser.GetQuotedStringLength(input, current, out quotedStringLength) !=
                        HttpParseResult.Parsed)
                    {
                        // We have a quote but an invalid quoted-string.
                        return false;
                    }
                    current = current + quotedStringLength;
                    parameterEndIndex = current - 1; // -1 because 'current' points to the char after the final '"'
                }
                else
                {
                    int whitespaceLength = HttpRuleParser.GetWhitespaceLength(input, current);

                    // We don't want trailing whitespaces to be considered part of the parameter blob. Increment
                    // 'parameterEndIndex' only if we don't have a whitespace. E.g. "Basic AbC=  , NTLM" should return
                    // "AbC=" as parameter ignoring the spaces before ','.
                    if (whitespaceLength == 0)
                    {
                        parameterEndIndex = current;
                        current++;
                    }
                    else
                    {
                        current = current + whitespaceLength;
                    }
                }
            }

            return true;
        }

        private static bool TryGetParametersEndIndex(string input, ref int parseEndIndex, ref int parameterEndIndex)
        {
            Contract.Requires(parseEndIndex < input.Length, "Expected string to have at least 1 char");
            Contract.Assert(input[parseEndIndex] == ',');

            int current = parseEndIndex;
            do
            {
                current++; // skip ',' delimiter

                bool separatorFound = false; // ignore value returned by GetNextNonEmptyOrWhitespaceIndex()
                current = HeaderUtilities.GetNextNonEmptyOrWhitespaceIndex(input, current, true, out separatorFound);
                if (current == input.Length)
                {
                    return true;
                }

                // Now we have to determine if after ',' we have a list of <name>=<value> pairs that are part of 
                // the auth scheme parameters OR if we have another auth scheme. Either way, after ',' we expect a 
                // valid token that is either the <name> in a <name>=<value> pair OR <scheme> of another scheme.
                int tokenLength = HttpRuleParser.GetTokenLength(input, current);
                if (tokenLength == 0)
                {
                    return false;
                }

                current = current + tokenLength;
                current = current + HttpRuleParser.GetWhitespaceLength(input, current);

                // If we reached the end of the string or the token is followed by anything but '=', then the parsed 
                // token is another scheme name. The string representing parameters ends before the token (e.g. 
                // "Digest a=b, c=d, NTLM": return scheme "Digest" with parameters string "a=b, c=d").
                if ((current == input.Length) || (input[current] != '='))
                {
                    return true;
                }

                current++; // skip '=' delimiter
                current = current + HttpRuleParser.GetWhitespaceLength(input, current);
                int valueLength = NameValueHeaderValue.GetValueLength(input, current);

                // After '<name>=' we expect a valid <value> (either token or quoted string)
                if (valueLength == 0)
                {
                    return false;
                }

                // Update parameter end index, since we just parsed a valid <name>=<value> pair that is part of the
                // parameters string.
                current = current + valueLength;
                parameterEndIndex = current - 1; // -1 because 'current' already points to the char after <value>
                current = current + HttpRuleParser.GetWhitespaceLength(input, current);
                parseEndIndex = current; // this essentially points to parameterEndIndex + whitespaces + next char

            } while ((current < input.Length) && (input[current] == ','));

            return true;
        }

        object ICloneable.Clone()
        {
            return new AuthenticationHeaderValue(this);
        }
    }
}
