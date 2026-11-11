import Testing
@testable import TelePilotMac

struct TrackRestorationTests {
    @Test func uniqueNameSurvivesChangedEngineID() {
        let tracks = [MediaTrack(id:7,name:"English",selected:true), MediaTrack(id:9,name:"Polish",selected:false)]
        #expect(RememberedTrack.match(tracks,id:7,name:"Polish")?.id == 9)
    }
    @Test func duplicateNamesKeepIDAndDisabledTrackRemainsDisabled() {
        let tracks = [MediaTrack(id:-1,name:"Disable",selected:false),MediaTrack(id:7,name:"Audio",selected:false),MediaTrack(id:9,name:"Audio",selected:true)]
        #expect(RememberedTrack.match(tracks,id:7,name:"Audio")?.id == 7)
        #expect(RememberedTrack.match(tracks,id:-1,name:"Audio")?.id == -1)
        #expect(RememberedTrack.match([],id:7,name:"Polish") == nil)
    }
}
