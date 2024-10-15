# OSMTileProxy

OSMTileProxy is a caching proxy for map tiles from Openstreetmap or another map tiles provider.

It exposes endpoint /tiles/{provider}/{level:int}/{x:int}/{y:int} returning an image for given tile coordinates. First it loads a tile from the provider, stores it to the disk cache, and then uses this image until it expires.

### Configuration

Configuration section `Tiles` in the `appsettings.json` contains everything required for proxying and caching.

```

    "Tiles": {
        "Cache": "/var/www/tiles",
        "Providers": [
            {
                "Id": "osm",
                "Url": "https://tile.openstreetmap.org/{0}/{1}/{2}.png",
                "UserAgent": "Your-user-agent",
                "ContentType": "image/png",
                "UseWebp": false,
                "MinZoom": 1,
                "MaxZoom": 19
            }
        ]
    }

```

Below is the explanation of the section parameters.

`Cache` - folder where tiles are cached, in development configration `appsettings.Development.json` it is c:\temp\tiles, make sure c:\temp exists or specify another location.  
`Providers` - array of provider parameters.  
`Providers:[i]:Id` - arbitrary identifier of the provider, used in the endpoint, for example /osm/2/1/1.  
`Providers:[i]:Url` - providers endpoint pattern, where {0} is level (map zoom), {1} - x-coordinate of a tile, {2} - y-coordinate of a tile.  
`Providers:[i]:UserAgent` - is mandatory for Openstreetmap service, specify your unique User-Agent.  
`Providers:[i]:ContentType` - value of Content-Type header in response, when `UseWebp` (see below) is `true` this header is always set to image/webp.  
`Providers:[i]:UseWebp` - optional parameter (default value is `false`). When `true`, OSMTileProxy converts tile images to webp format.  
`Providers:[i]:MinZoom` - optional parameter (default value is 1). Specifies minimum zoom level for tiles provider, used for validation.  
`Providers:[i]:MaxZoom` - optional parameter (default value is 19). Specifies maximum zoom level for tiles provider, used for validation.

### Deployment notes

If deployed as docker container, bind cache directory to your container, for example

```
docker run -p 8080:8080 --name osmtileproxy -v /var/www/tiles:/var/www/tiles -d osmtileproxy:latest
```