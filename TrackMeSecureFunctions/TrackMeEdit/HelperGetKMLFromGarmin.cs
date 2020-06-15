using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public class HelperGetKMLFromGarmin
    {
        public async Task<string> GetKMLAsync(KMLInfo kMLInfo)
        {
            string userUrl = $"{kMLInfo.InReachWebAddress}{CreateDateParameter(kMLInfo.d1, "d1")}{CreateDateParameter(kMLInfo.d2, "d2")}";
            string garminUrl = $"https://share.garmin.com/Feed/Share/{userUrl}";

            //get the data from garmin based on start date in every 5 minutes
            var http = new HttpClient();
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{kMLInfo.InReachWebPassword}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            HttpResponseMessage response = http.GetAsync(garminUrl).Result;
            return await response.Content.ReadAsStringAsync();
        }
        string CreateDateParameter(string dateTime, string param)
        {
            if (!string.IsNullOrEmpty(dateTime))
            {
                if (param == "d2")
                    return $"&{param}={dateTime}";
                if (param == "d1")
                    return $"?{param}={dateTime}";
            }
            return string.Empty;
        }
    }
}
