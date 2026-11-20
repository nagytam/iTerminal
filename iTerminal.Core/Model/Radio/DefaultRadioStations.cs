using System.Security.Cryptography;
using System.Text;

namespace iTerminal.Core.Model.Radio;

/// <summary>
/// The stations the list starts with before the person has saved one: popular internet radio from the US, the UK,
/// Germany, Switzerland and Hungary, across music, news, business and technology. The stream addresses are the ones
/// listed most often on radio-browser.info with a working check. Each station is an ordinary entry: the person can
/// change, favourite or remove it.
/// </summary>
public static class DefaultRadioStations
{
    private static readonly (string Name, string Url, string Genre)[] Seed =
    [
        // United States
        ("Classic Vinyl HD", "https://icecast.walmradio.com:8443/classic", "music, oldies"),
        ("101 Smooth Jazz", "http://jking.cdnstream1.com/b22139_128mp3", "music, smooth jazz"),
        ("Radio Paradise", "http://stream-uk1.radioparadise.com/aac-320", "music, eclectic"),
        ("SomaFM Groove Salad", "https://ice5.somafm.com/groovesalad-128-mp3", "music, ambient"),
        ("SomaFM Indie Pop Rocks!", "https://ice5.somafm.com/indiepop-128-aac", "music, indie"),
        (".977 Hitz", "http://14123.live.streamtheworld.com/977_HITS_SC", "music, hits"),
        ("NPR", "http://npr-ice.streamguys1.com/live.mp3", "news, public radio"),
        ("Fox News Radio", "https://live.amperwave.net/direct/foxnewsradio-foxnewsradioaac-imc", "news"),
        ("Bloomberg Radio New York", "http://20593.live.streamtheworld.com/WBBRNYAAC_SC", "business, news"),
        ("Jupiter Broadcasting Live", "http://n09.radiojar.com/0uk94cu0xrquv", "technology"),

        // United Kingdom
        ("BBC Radio 1", "http://as-hls-ww-live.akamaized.net/pool_01505109/live/ww/bbc_radio_one/bbc_radio_one.isml/bbc_radio_one-audio%3d128000.norewind.m3u8", "music, pop"),
        ("BBC Radio 2", "http://as-hls-ww-live.akamaized.net/pool_74208725/live/ww/bbc_radio_two/bbc_radio_two.isml/bbc_radio_two-audio%3d128000.norewind.m3u8", "music, adult contemporary"),
        ("BBC Radio 6 Music", "http://as-hls-ww-live.akamaized.net/pool_81827798/live/ww/bbc_6music/bbc_6music.isml/bbc_6music-audio%3d128000.norewind.m3u8", "music, alternative"),
        ("BBC Radio 4", "http://as-hls-ww-live.akamaized.net/pool_55057080/live/ww/bbc_radio_fourfm/bbc_radio_fourfm.isml/bbc_radio_fourfm-audio=128000.norewind.m3u8", "news, talk"),
        ("BBC Radio 5 Live", "http://as-hls-ww-live.akamaized.net/pool_89021708/live/ww/bbc_radio_five_live/bbc_radio_five_live.isml/bbc_radio_five_live-audio%3d128000.norewind.m3u8", "news, sport"),
        ("BBC World Service", "https://stream.live.vc.bbcmedia.co.uk/bbc_world_service", "news"),
        ("LBC News", "https://media-ssl.musicradio.com/LBCNewsUK", "news, talk"),
        ("talkSPORT", "https://radio.talksport.com/stream", "news, sport"),
        ("Capital UK", "https://media-ssl.musicradio.com/CapitalUK", "music, pop"),
        ("Classic FM", "http://media-ice.musicradio.com/ClassicFMMP3", "music, classical"),
        ("NTS Radio 1", "http://stream-relay-geo.ntslive.net/stream", "music, eclectic"),

        // Germany
        ("MANGORADIO", "https://mangoradio.stream.laut.fm/mangoradio", "music, variety"),
        ("SWR3", "https://liveradio.swr.de/sw282p3/swr3/play.mp3", "music, pop"),
        ("1LIVE", "https://wdr-1live-live.icecastssl.wdr.de/wdr/1live/live/mp3/128/stream.mp3", "music, pop"),
        ("Bayern 3", "https://dispatcher.rndfnk.com/br/br3/live/mp3/mid", "music, pop"),
        ("Antenne Bayern", "https://stream.antenne.de/antenne/stream/mp3", "music, hits"),
        ("TechnoBase.FM", "https://listener3.mp3.tb-group.fm/tb.mp3", "music, electronic"),
        ("Klassik Radio", "https://live.streams.klassikradio.de/klassikradio-deutschland/stream/mp3", "music, classical"),
        ("Deutschlandfunk", "https://st01.sslstream.dlf.de/dlf/01/128/mp3/stream.mp3", "news, culture"),
        ("Deutschlandfunk Kultur", "https://st02.sslstream.dlf.de/dlf/02/128/mp3/stream.mp3", "news, culture"),
        ("NIUS", "http://radio.nius.de/live", "news"),
        ("TechLiveRadio", "https://stream.laut.fm/techliveradio", "technology"),

        // Switzerland
        ("Radio SRF 3", "https://stream.srg-ssr.ch/m/drs3/aacp_96", "music, pop"),
        ("Couleur3", "http://stream.srg-ssr.ch/m/couleur3/aacp_96", "music, indie"),
        ("Radio Swiss Pop", "http://stream.srg-ssr.ch/srgssr/rsp/aac/96", "music, pop"),
        ("Radio Swiss Jazz", "https://stream.srg-ssr.ch/srgssr/rsj/aac/192", "music, jazz"),
        ("Radio Swiss Classic", "https://stream.srg-ssr.ch/srgssr/rsc_de/aac/192", "music, classical"),
        ("Energy Zürich", "http://broadcast.infomaniak.ch/energyzuerich-high.mp3", "music, pop"),
        ("Radio SRF 4 News", "http://stream.srg-ssr.ch/srgssr/srf4news/aac/96", "news"),

        // Hungary
        ("Retro Rádió", "https://icast.connectmedia.hu/5001/live.mp3", "music, 80s, 90s"),
        ("Rádió 1 Budapest", "http://icast.connectmedia.hu/5201/live.mp3", "music, hits"),
        ("Petőfi Rádió", "http://mr-stream.mediaconnect.hu/4737/mr2.aac", "music, alternative"),
        ("Bartók Rádió", "http://mr-stream.mediaconnect.hu/4742/mr3hq.mp3", "music, classical"),
        ("MegaDance Rádió", "http://megadanceradio.hopto.org:8000/livemega.mp3", "music, dance"),
        ("Oxygen The 80s Hits", "https://oxygenmusic.hu:8443/oxygenthe80shits", "music, 80s"),
        ("Klubrádió", "https://a7.asurahosting.com:8160/radio.mp3", "news, talk"),
        ("MR1 Kossuth Rádió", "https://mr-stream.connectmedia.hu/4736/mr1.mp3", "news, public radio"),
    ];

    /// <summary>The preseeded stations, each with an id that is the same on every machine.</summary>
    public static IReadOnlyList<RadioStation> All { get; } = Seed
        .Select(s => new RadioStation(IdFor(s.Url), s.Name, s.Url, s.Genre, IsFavourite: false))
        .ToList();

    /// <summary>A fixed id derived from the address, so a preseeded station keeps its id on every machine.</summary>
    private static Guid IdFor(string url) => new(MD5.HashData(Encoding.UTF8.GetBytes(url)));
}
