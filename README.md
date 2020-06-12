# TrackMe inReach app
#### https://track.ekstreem.ee/
### What is this app?  
This app is reading inReach KML feed from Garmin and displays user live location and-or historical tracks on the map.
This website has almost zero cost using serverless technologies in Azure.
### Functionality
* Turn on/off inReach Live tracking into *Today's track*. This track will be resetted every night.
* Set inReach Live tracking into predefined track with a specific name, start and end date. There could be many parallel tracks running in same time. For example:
  * One track is whole expedition with a duration of two months. "Everest expedition".
  * Other tracks could be like "Approaching into Everest basecamp", "Acclimatization", "Summit day" etc.
* This website allows to publish tracks even if Garmin's inReach site is protected by password.
* Publish your inReach historical tracks with a specific name, start and end date.
* You can have your own inReach device which is used by default for your tracks.
* You can have a rented inReach and each track can have their own inReach device.
* You can upload a predefined KML file as a planned track. This is very useful in cases like this:
  * People can see visually how far you are on your track.
  * If you have a support person then he/she knows exactly where to go to meet you.
### Public functionality
* People can follow you in the map.
* People can see all your predefined live or historical tracks. 
* People can locate themself on the map. This is very useful in cases like this: 
  * if you have a support person and he/she has to meet you in some place,
  * if someone has to find you but and you can't move by yourself.
* People can subscribe to your Live tracks to get notified by e-mail:
  * if you turn on inReach (Tracking has started)
  * if you send out any messages like "I am in South Pole", "I am having a lunch", "I am camping here" etc.
* People can click on your track in the map to see the length, duration and when this track has been started.
* People can read your inReach messages on the map and see the exact location.
* People can see the date, kilometers traveled and duration into each point from the start.
* People can see detailed points information in the map if the track duration is two days or less.
* People can click everywhere on the map and start navigation with Waze or Google maps.
### Technical Functionality
* If you have multiple parallel tracks then e-mail subscribers get notified only once.
* This application makes a query in every 5 minutes to Garmin site to check new points based on last point timestamp.
* In generally only last point is downloaded from Garmin.
* Up to two day tracks can have detailed points in a map showing exact time, speed, distance, duration, heading.
  * This is not enabled for longer tracks as it has great impact on file size loaded into map.
* Authentication for track management pages is based on Microsoft Account (Live ID).
### TrackMe Architecture

![Track Me Web Site](TrackMeWebSite.png)



