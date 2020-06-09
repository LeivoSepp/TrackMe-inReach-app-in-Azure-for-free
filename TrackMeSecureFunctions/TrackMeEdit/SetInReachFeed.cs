using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Web;
using System.Text;
using System.Globalization;
using System.Security.Claims;
using System.IO;
using Newtonsoft.Json;

namespace TrackMeSecureFunctions.TrackMeEdit
{
    public static class SetInReachFeed
    {
        private static HelperKMLParse helperKMLParse = new HelperKMLParse();

        //this function will remove all weird characters from the id field
        public static string UrlEncode(string value)
        {
            string reservedCharacters = " -!*'();:@&=+$,/?%#[]€£${}._~Ž^§½<>|";
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder();

            foreach (char @char in value)
            {
                if (reservedCharacters.IndexOf(@char) == -1)
                    sb.Append(@char);
            }
            return sb.ToString();
        }
        //this function replace all special characters to the closest unicode character
        //for example: ä->a õ->o etc
        public static string RemoveDiacritics(string s)
        {
            string normalizedString = s.Normalize(NormalizationForm.FormD);
            StringBuilder stringBuilder = new StringBuilder();

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    stringBuilder.Append(c);
            }
            return stringBuilder.ToString();
        }
        //taking POST formdata and saves it into CosmosDB
        [FunctionName("SetInReachFeed")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection"
                )]
                IAsyncCollector<KMLInfo> output,
            [CosmosDB(
                databaseName: "HomeIoTDB",
                collectionName: "GPSTracks",
                ConnectionStringSetting = "CosmosDBConnection",
                SqlQuery = "SELECT * FROM c WHERE c.groupid = 'user'"
            )] IEnumerable<InReachUser> users
            )
        {
            ClaimsPrincipal Identities = req.HttpContext.User;
            var checkUser = new HelperCheckUser();
            var LoggedInUser = checkUser.LoggedInUser(users, Identities);
            var IsAuthenticated = false;
            if (LoggedInUser.status == Status.ExistingUser)
                IsAuthenticated = true;

            if (IsAuthenticated)
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (requestBody != "")
                {
                    var fullTrack = JsonConvert.DeserializeObject<KMLInfo>(requestBody);
                    fullTrack.groupid = LoggedInUser.userWebId;

                    //1. replace ö->o ä->a etc
                    //2. first: UrlEncode is removing all weird charactes and spaces
                    //3. second: HttpUtility.UrlEncode is removing some not named weird characters, just in case
                    //4. third: UrlEncode again is removing possible %-marks
                    //setting id field only on initial track creation
                    if (string.IsNullOrEmpty(fullTrack.id))
                    {
                        string id = RemoveDiacritics(fullTrack.Title);
                        id = UrlEncode(HttpUtility.UrlEncode(UrlEncode(id)));
                        fullTrack.id = id;
                    }
                    fullTrack.LastPointTimestamp = "";

                    fullTrack.d1 = DateTime.Parse(fullTrack.d1).AddHours(-fullTrack.UserTimezone).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    fullTrack.d2 = DateTime.Parse(fullTrack.d2).AddDays(1).AddHours(-fullTrack.UserTimezone).ToString("yyyy-MM-ddTHH:mm:ssZ");

                    HelperGetKMLFromGarmin helperGetKMLFromGarmin = new HelperGetKMLFromGarmin();
                    //get feed grom garmin
                    var kmlFeedresult = await helperGetKMLFromGarmin.GetKMLAsync(fullTrack);
                    //parse and transform the feed and save to database
                    helperKMLParse.ParseKMLFile(kmlFeedresult, fullTrack, new List<Emails>());
                    await output.AddAsync(fullTrack);
                }
            }
            return new OkObjectResult(IsAuthenticated);
        }
    }
}