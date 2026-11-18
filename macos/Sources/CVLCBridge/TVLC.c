#include "TVLC.h"
#include <CoreFoundation/CFData.h>
#include <dlfcn.h>
#include <pthread.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>
#include <stdatomic.h>
#include <time.h>
typedef struct Track { int id; char *name; struct Track *next; } Track;
typedef struct SoftwareOutput SoftwareOutput;
struct TVLC {
    void *library, *core, *instance, *player, *events;
    int error_attached, end_attached;
    atomic_uint input_events;
    double ambiguous_since;
    int software_enabled;
    pthread_mutex_t frame_mutex;
    SoftwareOutput *software_output;
    uint64_t frame_sequence;
};

struct SoftwareOutput {
    TVLC *owner;
    void *write_pixels, *displayed_pixels;
    unsigned width, height, pitch;
    uint64_t sequence;
};

#define FN(t, n) ((t)dlsym(v->library, n))

static unsigned setup_software_video(void **opaque, char *chroma, unsigned *width,
                                     unsigned *height, unsigned *pitch, unsigned *lines) {
    if (*width == 0 || *height == 0) return 0;
    // Limit CPU conversion and the memory used by each pair of frame buffers.
    unsigned max_width = *width >= *height ? 1920 : 1080;
    unsigned max_height = *width >= *height ? 1080 : 1920;
    if (*width > max_width || *height > max_height) {
        if ((uint64_t)*width * max_height >= (uint64_t)*height * max_width) {
            *height = (unsigned)((uint64_t)*height * max_width / *width);
            *width = max_width;
        } else {
            *width = (unsigned)((uint64_t)*width * max_height / *height);
            *height = max_height;
        }
        if (*width == 0) *width = 1;
        if (*height == 0) *height = 1;
    }

    SoftwareOutput *output = calloc(1, sizeof(*output));
    if (!output) return 0;
    output->owner = *opaque;
    output->width = *width;
    output->height = *height;
    output->pitch = (*width * 4 + 31u) & ~31u;
    unsigned padded_lines = (*height + 31u) & ~31u;
    size_t bytes = (size_t)output->pitch * padded_lines;
    if (posix_memalign(&output->write_pixels, 32, bytes) != 0 ||
        posix_memalign(&output->displayed_pixels, 32, bytes) != 0) {
        free(output->write_pixels);
        free(output->displayed_pixels);
        free(output);
        return 0;
    }
    memset(output->write_pixels, 0, bytes);
    memset(output->displayed_pixels, 0, bytes);

    memcpy(chroma, "RV32", 4);
    pitch[0] = output->pitch;
    lines[0] = padded_lines;
    *opaque = output;
    TVLC *v = output->owner;
    pthread_mutex_lock(&v->frame_mutex);
    v->software_output = output;
    pthread_mutex_unlock(&v->frame_mutex);
    return 1;
}

static void *lock_software_video(void *opaque, void **planes) {
    SoftwareOutput *output = opaque;
    // VLC 3.0.24's vmem reads all five entries, even for packed RV32.
    for (int plane = 0; plane < 5; ++plane) planes[plane] = NULL;
    planes[0] = output->write_pixels;
    return output;
}

static void display_software_video(void *opaque, void *picture) {
    (void)picture;
    SoftwareOutput *output = opaque;
    TVLC *v = output->owner;
    // vmem prepares and displays on the same output thread. The other buffer
    // remains immutable while the player copies its latest displayed frame.
    pthread_mutex_lock(&v->frame_mutex);
    if (v->software_output == output) {
        void *previous = output->displayed_pixels;
        output->displayed_pixels = output->write_pixels;
        output->write_pixels = previous;
        output->sequence = ++v->frame_sequence;
    }
    pthread_mutex_unlock(&v->frame_mutex);
}

static void cleanup_software_video(void *opaque) {
    SoftwareOutput *output = opaque;
    TVLC *v = output->owner;
    pthread_mutex_lock(&v->frame_mutex);
    if (v->software_output == output) v->software_output = NULL;
    pthread_mutex_unlock(&v->frame_mutex);
    free(output->write_pixels);
    free(output->displayed_pixels);
    free(output);
}

CGImageRef tvlc_copy_software_frame(TVLC *v, uint64_t *sequence) {
    if (!v || !sequence) return NULL;
    pthread_mutex_lock(&v->frame_mutex);
    SoftwareOutput *output = v->software_output;
    if (!output || output->sequence == 0 || output->sequence == *sequence) {
        pthread_mutex_unlock(&v->frame_mutex);
        return NULL;
    }
    uint64_t latest = output->sequence;
    unsigned width = output->width, height = output->height, pitch = output->pitch;
    CFDataRef data = CFDataCreate(NULL, output->displayed_pixels, (CFIndex)((size_t)pitch * height));
    pthread_mutex_unlock(&v->frame_mutex);
    if (!data) return NULL;

    CGDataProviderRef provider = CGDataProviderCreateWithCFData(data);
    CFRelease(data);
    if (!provider) return NULL;
    CGColorSpaceRef color_space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGImageRef image = color_space ? CGImageCreate(width, height, 8, 32, pitch, color_space,
        kCGBitmapByteOrder32Little | kCGImageAlphaNoneSkipFirst, provider, NULL, true,
        kCGRenderingIntentDefault) : NULL;
    if (color_space) CGColorSpaceRelease(color_space);
    CGDataProviderRelease(provider);
    if (image) *sequence = latest;
    return image;
}

typedef void (*SetVideoCallbacks)(void *, void *(*)(void *, void **),
                                  void (*)(void *, void *, void *const *), void (*)(void *, void *), void *);
typedef void (*SetVideoFormatCallbacks)(void *, unsigned (*)(void **, char *, unsigned *, unsigned *,
                                                           unsigned *, unsigned *), void (*)(void *));

static int configure_video_output(TVLC *v, void *view, int software_video) {
    void (*set_view)(void *, void *) = FN(void (*)(void *, void *), "libvlc_media_player_set_nsobject");
    if (software_video || v->software_enabled) {
        SetVideoCallbacks set_callbacks = FN(SetVideoCallbacks, "libvlc_video_set_callbacks");
        SetVideoFormatCallbacks set_format = FN(SetVideoFormatCallbacks, "libvlc_video_set_format_callbacks");
        if (!set_callbacks || !set_format) return -1;
        if (software_video) {
            // set_nsobject resets vout selection, so clear the old drawable first.
            set_view(v->player, NULL);
            set_callbacks(v->player, lock_software_video, NULL, display_software_video, v);
            set_format(v->player, setup_software_video, cleanup_software_video);
        } else {
            set_callbacks(v->player, NULL, NULL, NULL, NULL);
            set_format(v->player, NULL, NULL);
        }
    }
    if (!software_video) set_view(v->player, view);
    v->software_enabled = software_video;
    return 0;
}

// LibVLC can replace Error with Ended when the failed input dies. Preserve the
// actual error event for this input; callbacks only write this atomic flag.
static void encountered_error(const void *event, void *data) {
    (void)event;
    TVLC *v = data;
    atomic_fetch_or_explicit(&v->input_events, 1, memory_order_release);
}
static void end_reached(const void *event, void *data) {
    (void)event;
    TVLC *v = data;
    atomic_fetch_or_explicit(&v->input_events, 2, memory_order_release);
}
static void discard(void *data, int level, const void *ctx, const char *fmt, va_list args) { (void)data;(void)level;(void)ctx;(void)fmt;(void)args; }
TVLC *tvlc_create(const char *library, const char *plugins) {
    TVLC *v = calloc(1, sizeof(TVLC)); if (!v) return NULL;
    atomic_init(&v->input_events, 0);
    char core[4096]; snprintf(core,sizeof(core),"%s",library);
    char *slash = strrchr(core,'/'); if (!slash) {free(v);return NULL;}
    if (pthread_mutex_init(&v->frame_mutex, NULL) != 0) { free(v); return NULL; }
    snprintf(slash+1,sizeof(core)-(size_t)(slash+1-core),"libvlccore.dylib");
    setenv("VLC_PLUGIN_PATH",plugins,1);
    v->core = dlopen(core,RTLD_NOW|RTLD_GLOBAL);
    v->library = dlopen(library,RTLD_NOW|RTLD_GLOBAL);
    if (!v->library || !v->core) { tvlc_destroy(v); return NULL; }
    const char *required[] = {"libvlc_new","libvlc_release","libvlc_log_set","libvlc_media_player_new","libvlc_media_player_release","libvlc_media_new_location","libvlc_media_release","libvlc_media_player_set_media","libvlc_media_player_set_nsobject","libvlc_media_player_play","libvlc_media_player_stop","libvlc_media_player_set_pause","libvlc_media_player_can_pause","libvlc_media_player_get_state","libvlc_audio_set_volume","libvlc_audio_set_mute","libvlc_audio_get_track_description","libvlc_video_get_spu_description","libvlc_track_description_list_release","libvlc_audio_get_track","libvlc_video_get_spu","libvlc_audio_set_track","libvlc_video_set_spu","libvlc_media_player_get_time","libvlc_media_player_set_time","libvlc_media_player_is_seekable","libvlc_media_player_event_manager","libvlc_event_attach","libvlc_event_detach"};
    for (unsigned i=0;i<sizeof(required)/sizeof(required[0]);i++) if (!dlsym(v->library,required[i])) {tvlc_destroy(v);return NULL;}
    const char *args[] = {"--quiet","--no-osd","--no-video-title-show","--no-media-library","--no-plugins-cache"};
    v->instance = FN(void *(*)(int,const char *const *),"libvlc_new")(5,args);
    if (!v->instance) {tvlc_destroy(v);return NULL;}
    FN(void (*)(void *,void (*)(void *,int,const void *,const char *,va_list),void *),"libvlc_log_set")(v->instance,discard,NULL);
    v->player = FN(void *(*)(void *),"libvlc_media_player_new")(v->instance);
    if (!v->player) {tvlc_destroy(v);return NULL;}
    v->events = FN(void *(*)(void *),"libvlc_media_player_event_manager")(v->player);
    if (!v->events || FN(int (*)(void *,int,void (*)(const void *,void *),void *),"libvlc_event_attach")(v->events,0x10a,encountered_error,v) != 0) {tvlc_destroy(v);return NULL;}
    v->error_attached = 1;
    if (FN(int (*)(void *,int,void (*)(const void *,void *),void *),"libvlc_event_attach")(v->events,0x109,end_reached,v) != 0) {tvlc_destroy(v);return NULL;}
    v->end_attached = 1;
    return v;
}
void tvlc_stop(TVLC *v) {
    if(v&&v->player) {
        FN(void (*)(void *),"libvlc_media_player_stop")(v->player);
        pthread_mutex_lock(&v->frame_mutex);
        v->software_output = NULL;
        pthread_mutex_unlock(&v->frame_mutex);
        atomic_store_explicit(&v->input_events,0,memory_order_release);
        v->ambiguous_since=0;
    }
}
void tvlc_destroy(TVLC *v) {
    if (!v) return;
    if (v->player) {
        tvlc_stop(v);
        if(v->error_attached)FN(void (*)(void *,int,void (*)(const void *,void *),void *),"libvlc_event_detach")(v->events,0x10a,encountered_error,v);
        if(v->end_attached)FN(void (*)(void *,int,void (*)(const void *,void *),void *),"libvlc_event_detach")(v->events,0x109,end_reached,v);
        FN(void (*)(void *),"libvlc_media_player_release")(v->player);
    }
    if (v->instance) FN(void (*)(void *),"libvlc_release")(v->instance);
    if (v->library) dlclose(v->library);
    if (v->core) dlclose(v->core);
    pthread_mutex_destroy(&v->frame_mutex);
    free(v);
}
int tvlc_play(TVLC *v,const char *url,void *view,const char *const *options,int count,int software_video){
    if(!v)return -1;
    void (*add_option)(void *,const char *) = FN(void (*)(void *,const char *),"libvlc_media_add_option");
    if(!add_option)return -1;
    tvlc_stop(v);
    // stop joins the previous input before a new attempt can clear its error.
    atomic_store_explicit(&v->input_events, 0, memory_order_release);
    v->ambiguous_since=0;
    void *m=FN(void *(*)(void *,const char *),"libvlc_media_new_location")(v->instance,url);
    if(!m)return -1;
    for(int i=0;i<count;i++) if(options[i])add_option(m,options[i]);
    FN(void (*)(void *,void *),"libvlc_media_player_set_media")(v->player,m);
    FN(void (*)(void *),"libvlc_media_release")(m);
    if (configure_video_output(v, view, software_video) != 0) return -1;
    return FN(int (*)(void *),"libvlc_media_player_play")(v->player);
}
// libvlc_media_stats_t ABI from the bundled, pinned LibVLC.
typedef struct MediaStats {
    int read_bytes; float input_bitrate; int demux_read_bytes; float demux_bitrate;
    int demux_corrupted, demux_discontinuity, decoded_video, decoded_audio;
    int displayed_pictures, lost_pictures, played_abuffers, lost_abuffers;
    int sent_packets, sent_bytes; float send_bitrate;
} MediaStats;
int tvlc_progress(TVLC *v,TVLCProgress *out){
    if(!v||!out)return 0;
    void *(*get_media)(void *)=FN(void *(*)(void *),"libvlc_media_player_get_media");
    int (*get_stats)(void *,MediaStats *)=FN(int (*)(void *,MediaStats *),"libvlc_media_get_stats");
    if(!get_media||!get_stats)return 0;
    void *media=get_media(v->player); if(!media)return 0;
    MediaStats stats={0}; int ok=get_stats(media,&stats);
    FN(void (*)(void *),"libvlc_media_release")(media);
    if(ok){out->read_bytes=stats.read_bytes;out->decoded_video=stats.decoded_video;out->displayed_video=stats.displayed_pictures;out->decoded_audio=stats.decoded_audio;out->played_audio=stats.played_abuffers;}
    return ok;
}
int64_t tvlc_length(TVLC *v){
    if(!v)return -1;
    int64_t (*f)(void *)=FN(int64_t (*)(void *),"libvlc_media_player_get_length");
    return f?f(v->player):-1;
}
int tvlc_video_track_count(TVLC *v){
    if(!v)return -1;
    int (*f)(void *)=FN(int (*)(void *),"libvlc_video_get_track_count");
    return f?f(v->player):-1;
}
int tvlc_audio_only(TVLC *v){
    if(!v)return 0;
    int (*f)(void *)=FN(int (*)(void *),"libvlc_video_get_track_count");
    return f&&f(v->player)==0;
}
void tvlc_pause(TVLC *v,int p){if(v)FN(void (*)(void *,int),"libvlc_media_player_set_pause")(v->player,p);}
int tvlc_can_pause(TVLC *v){return v?FN(int (*)(void *),"libvlc_media_player_can_pause")(v->player):0;}
int tvlc_state(TVLC *v){
    if(!v)return 0;
    int state=FN(int (*)(void *),"libvlc_media_player_get_state")(v->player);
    unsigned events=atomic_load_explicit(&v->input_events,memory_order_acquire);
    if(state==7 || (events&1))return 7;
    if(events&2)return 6;
    if(state!=6) {v->ambiguous_since=0;return state;}
    // State changes precede callbacks. Give a genuine EndReached callback one
    // bounded interval to arrive; an unconfirmed dead input is a failed open.
    struct timespec clock;
    clock_gettime(CLOCK_MONOTONIC,&clock);
    double now=(double)clock.tv_sec+(double)clock.tv_nsec/1e9;
    if(v->ambiguous_since==0)v->ambiguous_since=now;
    return now-v->ambiguous_since>=1 ? 7 : 1;
}
void tvlc_volume(TVLC *v,int x){if(v)FN(int (*)(void *,int),"libvlc_audio_set_volume")(v->player,x);}
void tvlc_mute(TVLC *v,int x){if(v)FN(void (*)(void *,int),"libvlc_audio_set_mute")(v->player,x);}
static Track *tracks(TVLC *v,int s){return v?FN(Track *(*)(void *),s?"libvlc_video_get_spu_description":"libvlc_audio_get_track_description")(v->player):NULL;}
static void release(TVLC *v,Track *t){if(t)FN(void (*)(Track *),"libvlc_track_description_list_release")(t);}
int tvlc_track_count(TVLC *v,int s){Track *a=tracks(v,s),*t=a;int n=0;for(;t;t=t->next)n++;release(v,a);return n;}
int tvlc_track_id(TVLC *v,int s,int i){Track *a=tracks(v,s),*t=a;for(;t&&i>0;i--)t=t->next;int n=t?t->id:-1;release(v,a);return n;}
char *tvlc_track_name(TVLC *v,int s,int i){Track *a=tracks(v,s),*t=a;for(;t&&i>0;i--)t=t->next;char *n=t?strdup(t->name?t->name:""):NULL;release(v,a);return n;}
void tvlc_free(void *p){free(p);}
int tvlc_current_track(TVLC *v,int s){return v?FN(int (*)(void *),s?"libvlc_video_get_spu":"libvlc_audio_get_track")(v->player):-1;}
int tvlc_set_track(TVLC *v,int s,int i){return v?FN(int (*)(void *,int),s?"libvlc_video_set_spu":"libvlc_audio_set_track")(v->player,i):-1;}
int64_t tvlc_time(TVLC *v){return v?FN(int64_t (*)(void *),"libvlc_media_player_get_time")(v->player):-1;}
int tvlc_seek(TVLC *v,int64_t ms){if(!v||!FN(int (*)(void *),"libvlc_media_player_is_seekable")(v->player))return -1;FN(void (*)(void *,int64_t),"libvlc_media_player_set_time")(v->player,ms<0?0:ms);return 0;}
