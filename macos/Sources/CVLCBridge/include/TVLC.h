#pragma once
#include <stdint.h>
typedef struct TVLC TVLC;
TVLC *tvlc_create(const char *library, const char *plugins);
void tvlc_destroy(TVLC *);
int tvlc_play(TVLC *, const char *url, void *view, const char *const *options, int option_count);
typedef struct TVLCProgress {
    int read_bytes, decoded_video, displayed_video, decoded_audio, played_audio;
} TVLCProgress;
int tvlc_progress(TVLC *, TVLCProgress *);
int64_t tvlc_length(TVLC *);
int tvlc_audio_only(TVLC *);
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
