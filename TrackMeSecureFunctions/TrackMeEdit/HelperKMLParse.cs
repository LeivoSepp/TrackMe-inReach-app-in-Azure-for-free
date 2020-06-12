using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using GeoCoordinatePortable;

//google map icons: http://kml4earth.appspot.com/icons.html

namespace TrackMeSecureFunctions.TrackMeEdit
{
    class HelperKMLParse
    {
        //Add XML declaration, as it is automatically removed
        const string xmlString = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";

        //newBalloon defines the the look and feel if user clicks on the tracking point
        const string newBalloon = "<table style='width:180'>" +
            "<tr><td colspan='2'><b>Time: $[Time]</b></td></tr>" +
            "<tr><td>Speed</td><td> $[Velocity] </td></tr>" +
            "<tr><td>Distance</td><td> $[Distance] </td></tr>" +
            "<tr><td>Time</td><td> $[TimeElapsed] </td></tr>" +
            "<tr><td>Elevation</td><td> $[Elevation] </td></tr>" +
            "<tr><td>Heading</td><td> $[Course] </td></tr>" +
            "<tr><td colspan='2' style='border:1px solid lightblue;'> $[Text] </td></tr>" +
            "<tr><td colspan='2'> $[Latitude], $[Longitude] </td></tr>" +
            "</table>";

        private string GetHeading(int heading, out int idx)
        {
            var directions = new string[] {
                "n", "nne", "ne", "ene", "e", "ese", "se", "sse", "s", "ssw", "sw", "wsw", "w", "wnw", "nw", "nnw", "n"
                };
            var index = (heading + 11) / 22;
            idx = index;
            return directions[index];
        }
        //inReach default events
        static List<string> InReachEvents = new List<string>()
        {
            "Tracking turned on from device.",
            "Quick Text to MapShare received",
            "Msg to shared map received",
            "Text message received",
            "Append MapShare to txt msg code received",
            "Tracking turned off from device"
        };

        class StyleKeyword
        {
            public StyleKeyword(string StyleName, string LogoName, List<string> Keywords)
            {
                this.StyleName = StyleName;
                this.LogoName = LogoName;
                this.Keywords = Keywords;
            }
            public string StyleName;
            public string LogoName;
            public List<string> Keywords = new List<string>();
        }
        List<StyleKeyword> StyleKeywords = new List<StyleKeyword>()
        {
            new StyleKeyword("trackingStarted", "hiker.png",
                new List<string>{ InReachEvents[0]}),
            new StyleKeyword("messageReceived", "post_office.png",
                new List<string>{ InReachEvents[1], InReachEvents[2] }),
            new StyleKeyword("campStyle", "campground.png",
                new List<string>{"camp", "campsite", "tent", "bivouac", "laager" }),
            new StyleKeyword("finishStyle", "parking_lot.png",
                new List<string>{"finish", "complete", "end", "close", "parking", "stop" }),
            new StyleKeyword("summitStyle", "mountains.png",
                new List<string>{"summit", "top", "peak", "mountain"}),
            new StyleKeyword("picnicStyle", "picnic.png",
                new List<string>{"picnic", "meal", "cooking", "bbq", "barbeque", "cookout" }),
            new StyleKeyword("cameraStyle", "camera.png",
                new List<string>{"beautiful", "beauty spot", "exciting", "nice", "great", "lovely", "pretty", "cool"})
        };

        //add new style into KMLfeed
        private void AddKMLStyle(XElement Document, XNamespace defaultns, string styleName, string newBalloon, string iconUrl)
        {
            Document.Add(new XElement(defaultns + "Style",
                new XAttribute("id", styleName),
                    new XElement(defaultns + "BalloonStyle",
                        new XElement(defaultns + "text",
                        newBalloon
                        )
                    ),
                    new XElement(defaultns + "IconStyle",
                        new XElement(defaultns + "Icon",
                            new XElement(defaultns + "href",
                            iconUrl)
                        )
                    )
                )
            );
        }
        XElement AddStyles(XElement Document, XNamespace defaultns)
        {
            //Add style elements for 16 different heading (google arrow icons)
            for (int i = 0; i <= 15; i++)
            {
                var iconUrl = "http://earth.google.com/images/kml-icons/track-directional/track-" + i.ToString() + ".png";
                AddKMLStyle(Document, defaultns, i.ToString(), newBalloon, iconUrl);
            }
            //add additional style elements for each predefined keyword
            foreach (var styleKeyword in StyleKeywords)
            {
                AddKMLStyle(Document, defaultns, styleKeyword.StyleName, newBalloon, "http://maps.google.com/mapfiles/kml/shapes/" + styleKeyword.LogoName);
            }
            return Document;
        }
        //get server-based date-time and calculate it to specific timezone
        //public DateTime GetLocalTime(string timezone)
        //{
        //    //timezone: "FLE Standard Time"
        //    var TimezoneCorrected = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        //    return TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimezoneCorrected);
        //}
        string ReturnValue(XElement element)
        {
            var checkFolder = element;
            if (checkFolder != null)
                return element.Value;
            return string.Empty;
        }
        string NewLastTimestamp(IEnumerable<XElement> placemarks, XmlNamespaceManager ns)
        {
            DateTime newLastPoint = new DateTime(2000, 1, 1);
            DateTime compareDate;
            string compareString;
            foreach (var item in placemarks)
            {
                //Placemarks without ExtendedData not proceed
                var check = item.XPathSelectElement("./kml:ExtendedData", ns);
                if (check != null)
                {
                    compareString = ReturnValue(item.XPathSelectElement("./kml:TimeStamp/kml:when", ns));
                    compareDate = DateTime.Parse(compareString).ToUniversalTime();
                    if (compareDate > newLastPoint)
                        newLastPoint = compareDate;
                }
            }
            return newLastPoint.ToString("o");
        }
        bool IsDate1BiggerThanDate2(string NewLastPointTime, string LastPointTimestamp)
        {
            DateTime dTLast, dTNew;
            if (string.IsNullOrEmpty(LastPointTimestamp))
                dTLast = new DateTime(2000, 1, 1);
            else
                dTLast = DateTime.SpecifyKind(DateTime.Parse(LastPointTimestamp, CultureInfo.CreateSpecificCulture("en-US")), DateTimeKind.Utc);
            if (string.IsNullOrEmpty(NewLastPointTime))
                dTNew = DateTime.UtcNow.ToUniversalTime();
            else
                dTNew = DateTime.Parse(NewLastPointTime).ToUniversalTime();
            if (dTNew > dTLast)
                return true;
            return false;
        }
        public bool IsThereNewPoints(string kmlFeedresult, KMLInfo track)
        {
            string LastPointts = track.LastPointTimestamp;
            XDocument xmlTrack = XDocument.Parse(kmlFeedresult);
            XmlNamespaceManager ns = new XmlNamespaceManager(new NameTable());
            ns.AddNamespace("kml", "http://www.opengis.net/kml/2.2");

            //set placemark as a root element
            var placemarks = xmlTrack.XPathSelectElements("//kml:Placemark", ns);
            track.LastPointTimestamp = NewLastTimestamp(placemarks, ns);
            if (IsDate1BiggerThanDate2(track.LastPointTimestamp, LastPointts))
                return true;
            return false;
        }
        public bool ParseKMLFile(string kmlFeedresult, KMLInfo fullTrack, List<Emails> emails, InReachUser user = null, string webSiteUrl = "")
        {
            //open and parse KMLfeed
            XDocument xmlTrack = XDocument.Parse(kmlFeedresult);
            XmlNamespaceManager ns = new XmlNamespaceManager(new NameTable());
            ns.AddNamespace("kml", "http://www.opengis.net/kml/2.2");
            var defaultns = xmlTrack.Root.GetDefaultNamespace();
            XDocument xmlLineString;
            XDocument xmlPlacemarks;
            XDocument xmlPlacemarksWithMessages;

            XElement NewPlacemark =
                new XElement(defaultns + "Folder",
                new XElement(defaultns + "Placemark",
                new XElement(defaultns + "ExtendedData",
                new XElement(defaultns + "Data",
                    new XAttribute("name", "Time"),
                        new XElement(defaultns + "value")),
                new XElement(defaultns + "Data",
                    new XAttribute("name", "Velocity"),
                        new XElement(defaultns + "value")),
                new XElement(defaultns + "Data",
                    new XAttribute("name", "Distance"),
                        new XElement(defaultns + "value")),
                new XElement(defaultns + "Data",
                    new XAttribute("name", "TimeElapsed"),
                        new XElement(defaultns + "value")),
                new XElement(defaultns + "Data",
                    new XAttribute("name", "Elevation"),
                        new XElement(defaultns + "value")),
                new XElement(defaultns + "Data",
                    new XAttribute("name", "Course"),
                        new XElement(defaultns + "value")),
                new XElement(defaultns + "Data",
                    new XAttribute("name", "Text"),
                        new XElement(defaultns + "value")),
                new XElement(defaultns + "Data",
                    new XAttribute("name", "Latitude"),
                        new XElement(defaultns + "value")),
                new XElement(defaultns + "Data",
                    new XAttribute("name", "Longitude"),
                        new XElement(defaultns + "value"))
                ),
                new XElement(defaultns + "styleUrl"),
                new XElement(defaultns + "Point",
                    new XElement(defaultns + "coordinates")
            )));

            XElement newDoc = new XElement(defaultns + "kml", new XElement(defaultns + "Document"));

            XElement newLineString =
                new XElement(defaultns + "Folder",
                    new XElement(defaultns + "Placemark",
                        new XElement(defaultns + "name"),
                        new XElement(defaultns + "description"),
                        new XElement(defaultns + "LineString",
                            new XElement(defaultns + "coordinates"
                ))));

            //create Xdocuments if they are empty
            if (!string.IsNullOrEmpty(fullTrack.PlacemarksAll))
                xmlPlacemarks = XDocument.Parse(fullTrack.PlacemarksAll);
            else
            {
                xmlPlacemarks = new XDocument(new XElement(newDoc));
                AddStyles(xmlPlacemarks.XPathSelectElement("//kml:Document", ns), defaultns);
            }
            if (!string.IsNullOrEmpty(fullTrack.PlacemarksWithMessages))
                xmlPlacemarksWithMessages = XDocument.Parse(fullTrack.PlacemarksWithMessages);
            else
            {
                xmlPlacemarksWithMessages = new XDocument(new XElement(newDoc));
                AddStyles(xmlPlacemarksWithMessages.XPathSelectElement("//kml:Document", ns), defaultns);
            }
            if (!string.IsNullOrEmpty(fullTrack.LineString))
                xmlLineString = XDocument.Parse(fullTrack.LineString);
            else
            {
                xmlLineString = new XDocument(new XElement(newDoc));
                var documentLineString = xmlLineString.XPathSelectElement("//kml:Document", ns);
                documentLineString.Add(newLineString);
            }

            var documentPlacemarkMessages = xmlPlacemarksWithMessages.XPathSelectElement("//kml:Document", ns);
            var documentPlacemark = xmlPlacemarks.XPathSelectElement("//kml:Document", ns);

            //set placemark as a root element            
            var placemarks = xmlTrack.XPathSelectElements("//kml:Placemark", ns);

            double lastLatitude = fullTrack.LastLatitude;
            double lastLongitude = fullTrack.LastLongitude;
            double totalDistance = fullTrack.LastTotalDistance;
            TimeSpan totalTime = new TimeSpan();
            TimeSpan.TryParse(fullTrack.LastTotalTime, out totalTime);

            DateTime lastDate = new DateTime();
            var lineStringMessage = string.Empty;
            DateTime trackStarted = new DateTime();
            var LastPointTimestamp = string.Empty;
            //iterate through each Placemark
            foreach (var placemark in placemarks)
            {
                //Placemarks without ExtendedData not proceed
                var check = placemark.XPathSelectElement("./kml:ExtendedData", ns);
                if (check != null)
                {
                    //copy coordinates
                    NewPlacemark.XPathSelectElement("//kml:Point/kml:coordinates", ns).Value =
                        placemark.XPathSelectElement("./kml:Point/kml:coordinates", ns).Value;
                    //copy LatLon
                    var thisLatitude =
                        NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Latitude']/kml:value", ns).Value =
                        placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Latitude']/kml:value", ns).Value;
                    var thisLongitude =
                        NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Longitude']/kml:value", ns).Value =
                        placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Longitude']/kml:value", ns).Value;

                    //calculate distance
                    double.TryParse(thisLatitude, NumberStyles.Any, CultureInfo.InvariantCulture, out double thisLatitudeDouble);
                    double.TryParse(thisLongitude, NumberStyles.Any, CultureInfo.InvariantCulture, out double thisLongitudeDouble);
                    GeoCoordinate pin1 = new GeoCoordinate(thisLatitudeDouble, thisLongitudeDouble);
                    GeoCoordinate pin2 = new GeoCoordinate(lastLatitude, lastLongitude);
                    if (lastLatitude != 0 && lastLongitude != 0)
                        totalDistance += pin1.GetDistanceTo(pin2) / 1000;
                    lastLatitude = thisLatitudeDouble;
                    lastLongitude = thisLongitudeDouble;
                    var distance = totalDistance.ToString("0") + " km";
                    NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Distance']/kml:value", ns).Value = distance;

                    //select Speed element end remove fraction after comma: 12 km/m
                    string speed = placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Velocity']/kml:value", ns).Value;
                    speed = speed.Substring(0, speed.IndexOf(".")) + " km/h";
                    NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Velocity']/kml:value", ns).Value = speed;

                    //select Course element and transform it compass style heading and remove fractions
                    string heading = placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Course']/kml:value", ns).Value;
                    heading = heading.Substring(0, heading.IndexOf("."));
                    Int32.TryParse(heading, out int h);
                    //setting placemark to use style according to heading: NE 45°
                    heading = GetHeading(h, out int idx).ToUpper() + " " + heading + "°";
                    NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Course']/kml:value", ns).Value = heading;
                    NewPlacemark.XPathSelectElement("//kml:styleUrl", ns).Value = $"#{idx}";

                    //select Elevation element end remove fraction after comma: 124 m
                    string elevation = placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Elevation']/kml:value", ns).Value;
                    elevation = elevation.Substring(0, elevation.IndexOf(".")) + " m";
                    NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Elevation']/kml:value", ns).Value = elevation;

                    //select Time element and covert
                    var dateTimeString = placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Time']/kml:value", ns).Value;
                    DateTime dT = DateTime.Parse(dateTimeString, CultureInfo.CreateSpecificCulture("en-US"));
                    var dateTimeToPlacemark = $"{dT:HH:mm dd.MM.yyyy}";
                    NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Time']/kml:value", ns).Value = dateTimeToPlacemark;

                    //select lastpoint timestamp. The nevest placemark is always the last placemark.
                    LastPointTimestamp = placemark.XPathSelectElement("./kml:TimeStamp/kml:when", ns).Value;

                    //calculate total time
                    if (lastDate != DateTime.MinValue)
                        totalTime += dT.Subtract(lastDate);
                    lastDate = dT;
                    var totalTimeStr = $"{totalTime:%d} day(s) {totalTime:%h\\:mm}";
                    NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'TimeElapsed']/kml:value", ns).Value = totalTimeStr;

                    //select events and messages
                    string textValue = placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Text']/kml:value", ns).Value;
                    string eventType = placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Event']/kml:value", ns).Value;
                    NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Text']/kml:value", ns).Value = textValue;

                    //check if the eventType or receivedText contains keywords, if yes, then attach the style to the placemark
                    foreach (var styleKeyword in StyleKeywords)
                    {
                        if (styleKeyword.Keywords.Any(eventType.Contains) || styleKeyword.Keywords.Any(textValue.ToLower().Contains))
                            NewPlacemark.XPathSelectElement("//kml:styleUrl", ns).Value = $"#{styleKeyword.StyleName}";
                    }
                    //if tracking turned on on device then copy Event into Text field
                    if (eventType == InReachEvents[0])
                    {
                        NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Text']/kml:value", ns).Value = InReachEvents[0];
                        trackStarted = lastDate;
                        if (string.IsNullOrEmpty(fullTrack.TrackStartTime))
                            fullTrack.TrackStartTime = trackStarted.ToString("HH:mm dd.MM.yyyy");
                    }

                    //get the sender name as a Garmin Map Display Name. Not used.
                    //var senderName = placemark.XPathSelectElement("./kml:ExtendedData/kml:Data[@name = 'Map Display Name']/kml:value", ns).Value;

                    var inReachMessage = NewPlacemark.XPathSelectElement("//kml:ExtendedData/kml:Data[@name = 'Text']/kml:value", ns).Value;
                    lineStringMessage = $"Track started on {fullTrack.TrackStartTime}.<br>Total distance traveled { distance} in { totalTimeStr}.";
                    var eMailMessage = $"Hello, <br><br><h3>{user.name} is on {fullTrack.Title}.</h3>" +
                        $"What just happened? <b>{inReachMessage}</b>.<br>" +
                        $"{lineStringMessage}<br>" +
                        $"Follow me on the map <a href='{webSiteUrl}/{fullTrack.groupid}?id={fullTrack.id}'></a>{webSiteUrl}/{fullTrack.groupid}<br><br>" +
                        $"This message was sent in {lastDate:HH:mm dd.MM.yyyy}, at location LatLon: {lastLatitude}, {lastLongitude}. " +
                        $"<a href='https://www.google.com/maps/search/?api=1&query={lastLatitude},{lastLongitude}'>Open in google maps</a>.<br><br>" +
                        $"Best regards,<br>Whoever is carrying this device.<br><br>" +
                        $"<small>Disclaimer<br>" +
                        $"You are getting this e-mail because you subscribed to receive {user.name} inReach messages.<br>" +
                        $"Click here to unsubscribe:<a href='{webSiteUrl}/unsubscribe?userWebId={fullTrack.groupid}'>Remove me from {user.name} inReach notifications</a>.<br>" +
                        $"Sorry! It's not working yet. You cannot unsubscribe. You have to follow me forever.</small>";
                    var eMailSubject = $"{user.name} at {lastDate:HH:mm}: {inReachMessage}";
                    //if inReach has sent out a message then add a Placemark
                    if (InReachEvents.Any(eventType.Contains))
                    {
                        //documentPlacemark.Add(NewPlacemark);
                        documentPlacemarkMessages.Add(new XElement(NewPlacemark));
                        emails.Add(new Emails
                        {
                            EmailBody = eMailMessage,
                            EmailSubject = eMailSubject,
                            UserWebId = fullTrack.groupid,
                            DateTime = dateTimeString,
                            EmailFrom = user.email,
                            Name = user.name,
                            EmailTo = user.subscibers
                        });
                    }
                    //add full placemarks only for short less than 1 day tracks 
                    if (!fullTrack.IsLongTrack)
                        documentPlacemark.Add(new XElement(NewPlacemark));
                    //else
                    //{

                    //}
                }
                else
                {
                    //build a linestring, add new coordinates into end of existing list
                    string NewCoordinates = ReturnValue(xmlTrack.XPathSelectElement("//kml:Placemark/kml:LineString/kml:coordinates", ns));
                    string Coordinates = ReturnValue(xmlLineString.XPathSelectElement("//kml:Placemark/kml:LineString/kml:coordinates", ns));
                    Coordinates = Coordinates + "\r\n" + NewCoordinates;
                    xmlLineString.XPathSelectElement("//kml:Placemark/kml:LineString/kml:coordinates", ns).Value = Coordinates;
                    xmlLineString.XPathSelectElement("//kml:Placemark/kml:name", ns).Value = fullTrack.Title;
                    xmlLineString.XPathSelectElement("//kml:Placemark/kml:description", ns).Value = lineStringMessage;
                }
            }
            fullTrack.LastTotalDistance = totalDistance;
            fullTrack.LastLatitude = lastLatitude;
            fullTrack.LastLongitude = lastLongitude;
            fullTrack.LastTotalTime = totalTime.ToString();
            fullTrack.LastPointTimestamp = LastPointTimestamp;
            fullTrack.PlacemarksWithMessages = xmlString + xmlPlacemarksWithMessages.ToString();
            fullTrack.LineString = xmlString + xmlLineString.ToString();
            //add full placemarks only for short less than 1 day tracks 
            if (!fullTrack.IsLongTrack)
                fullTrack.PlacemarksAll = xmlString + xmlPlacemarks.ToString();

            return true;
        }
    }

    public class KMLInfo
    {
        public string id { get; set; }
        public string Title { get; set; }
        public string d1 { get; set; }
        public string d2 { get; set; }
        public string InReachWebAddress { get; set; }
        public string InReachWebPassword { get; set; }
        public string groupid { get; set; }
        public int UserTimezone { get; set; }
        public string LastPointTimestamp { get; set; }
        public double LastLatitude { get; set; }
        public double LastLongitude { get; set; }
        public double LastTotalDistance { get; set; }
        public string LastTotalTime { get; set; }
        public string TrackStartTime { get; set; }
        public string LastPoint { get; set; }
        public bool IsLongTrack { get; set; }
        public string PlannedTrack { get; set; }
        public string PlacemarksAll { get; set; }
        public string PlacemarksWithMessages { get; set; }
        public string LineString { get; set; }
        public string _self { get; set; }
    }

    public class Emails
    {
        public string EmailSubject { get; set; }
        public string EmailBody { get; set; }
        public string UserWebId { get; set; }
        public string DateTime { get; set; }
        public string Name { get; set; }
        public string EmailFrom { get; set; }
        public string[] EmailTo { get; set; }
    }
}
