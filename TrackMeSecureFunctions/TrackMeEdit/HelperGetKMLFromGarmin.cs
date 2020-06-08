using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public class HelperGetKMLFromGarmin
    {
        public async Task<string> GetKMLAsync(KMLInfo getTrack)
        {
            string userUrl = $"{getTrack.InReachWebAddress}{CreateDateParameter(getTrack.d1, "d1", getTrack.UserTimezone)}{CreateDateParameter(getTrack.d2, "d2", getTrack.UserTimezone)}";
            string garminUrl = $"https://share.garmin.com/Feed/Share/{userUrl}";

            //get the data from garmin based on start date in every 5 minutes
            var http = new HttpClient();
            var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{getTrack.InReachWebPassword}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
            HttpResponseMessage response = http.GetAsync(garminUrl).Result;
            return await response.Content.ReadAsStringAsync();
        }
        string CreateDateParameter(string dateTime, string param, int UserTimezone)
        {
            //add one day into date, required for garmin query
            if (!string.IsNullOrEmpty(dateTime))
            {
                DateTime dT = DateTime.Parse(dateTime).ToUniversalTime().AddHours(UserTimezone);
                if (param == "d2")
                {
                    dT = dT.AddDays(1);
                    return $"&{param}={dT:o}";
                }
                if (param == "d1")
                    return $"?{param}={dT:o}";
            }
            return string.Empty;
        }
    }
}
