#pragma once
#include <CoreGraphics/CGImage.h>
#include <stdint.h>
typedef struct TVLC TVLC;
int tvlc_supports_accelerated_video(void);
TVLC *tvlc_create(const char *library, const char *plugins);
void tvlc_destroy(TVLC *);
int tvlc_play(TVLC *, const char *url, void *view, const char *const *options, int option_count, int software_video);
// Initialize sequence to zero. A new frame updates it and optionally returns the
// presentation width/height ratio. The result is null if there is no new frame.
CGImageRef tvlc_copy_software_frame(TVLC *, uint64_t *sequence, double *display_aspect) CF_RETURNS_RETAINED;
typedef struct TVLCProgress {
    int read_bytes, decoded_video, displayed_video, decoded_audio, played_audio;
} TVLCProgress;
int tvlc_progress(TVLC *, TVLCProgress *);
int64_t tvlc_length(TVLC *);
int tvlc_audio_only(TVLC *);
int tvlc_video_track_count(TVLC *);
void tvlc_stop(TVLC *);
void tvlc_pause(TVLC *, int paused);
int tvlc_can_pause(TVLC *);
int tvlc_state(TVLC *);
void tvlc_volume(TVLC *, int volume);
void tvlc_mute(TVLC *, int muted);
int tvlc_track_count(TVLC *, int subtitles);
int tvlc_track_id(TVLC *, int subtitles, int index);
char *tvlc_track_name(TVLC *, int subtitles, int index);
void tvlc_free(void *);
int tvlc_current_track(TVLC *, int subtitles);
int tvlc_set_track(TVLC *, int subtitles, int id);
int64_t tvlc_time(TVLC *);
int tvlc_seek(TVLC *, int64_t milliseconds);
