#include "TVLC.h"
#include <dlfcn.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <stdarg.h>
typedef struct Track { int id; char *name; struct Track *next; } Track;
struct TVLC { void *library, *core, *instance, *player; };
#define FN(t, n) ((t)dlsym(v->library, n))
static void discard(void *data, int level, const void *ctx, const char *fmt, va_list args) { (void)data;(void)level;(void)ctx;(void)fmt;(void)args; }
TVLC *tvlc_create(const char *library, const char *plugins) {
    TVLC *v = calloc(1, sizeof(TVLC)); if (!v) return NULL;
    char core[4096]; snprintf(core,sizeof(core),"%s",library);
    char *slash = strrchr(core,'/'); if (!slash) {free(v);return NULL;}
    snprintf(slash+1,sizeof(core)-(size_t)(slash+1-core),"libvlccore.dylib");
    setenv("VLC_PLUGIN_PATH",plugins,1);
    v->core = dlopen(core,RTLD_NOW|RTLD_GLOBAL);
    v->library = dlopen(library,RTLD_NOW|RTLD_GLOBAL);
    if (!v->library || !v->core) { tvlc_destroy(v); return NULL; }
    const char *required[] = {"libvlc_new","libvlc_release","libvlc_log_set","libvlc_media_player_new","libvlc_media_player_release","libvlc_media_new_location","libvlc_media_release","libvlc_media_player_set_media","libvlc_media_player_set_nsobject","libvlc_media_player_play","libvlc_media_player_stop","libvlc_media_player_set_pause","libvlc_media_player_can_pause","libvlc_media_player_get_state","libvlc_audio_set_volume","libvlc_audio_set_mute","libvlc_audio_get_track_description","libvlc_video_get_spu_description","libvlc_track_description_list_release","libvlc_audio_get_track","libvlc_video_get_spu","libvlc_audio_set_track","libvlc_video_set_spu","libvlc_media_player_get_time","libvlc_media_player_set_time","libvlc_media_player_is_seekable"};
    for (unsigned i=0;i<sizeof(required)/sizeof(required[0]);i++) if (!dlsym(v->library,required[i])) {tvlc_destroy(v);return NULL;}
    const char *args[] = {"--quiet","--no-osd","--no-video-title-show","--no-media-library","--no-plugins-cache"};
    v->instance = FN(void *(*)(int,const char *const *),"libvlc_new")(5,args);
    if (!v->instance) {tvlc_destroy(v);return NULL;}
    FN(void (*)(void *,void (*)(void *,int,const void *,const char *,va_list),void *),"libvlc_log_set")(v->instance,discard,NULL);
    v->player = FN(void *(*)(void *),"libvlc_media_player_new")(v->instance);
    if (!v->player) {tvlc_destroy(v);return NULL;} return v;
}
void tvlc_stop(TVLC *v) { if(v&&v->player) FN(void (*)(void *),"libvlc_media_player_stop")(v->player); }
void tvlc_destroy(TVLC *v) {if(!v)return; if(v->player) {tvlc_stop(v);FN(void (*)(void *),"libvlc_media_player_release")(v->player);}if(v->instance)FN(void (*)(void *),"libvlc_release")(v->instance);if(v->library)dlclose(v->library);if(v->core)dlclose(v->core);free(v);}
int tvlc_play(TVLC *v,const char *url,void *view,const char *const *options,int count){
    if(!v)return -1;
    void (*add_option)(void *,const char *) = FN(void (*)(void *,const char *),"libvlc_media_add_option");
    if(!add_option)return -1;
    tvlc_stop(v);
    void *m=FN(void *(*)(void *,const char *),"libvlc_media_new_location")(v->instance,url);
    if(!m)return -1;
    for(int i=0;i<count;i++) if(options[i])add_option(m,options[i]);
    FN(void (*)(void *,void *),"libvlc_media_player_set_media")(v->player,m);
    FN(void (*)(void *),"libvlc_media_release")(m);
    FN(void (*)(void *,void *),"libvlc_media_player_set_nsobject")(v->player,view);
    return FN(int (*)(void *),"libvlc_media_player_play")(v->player);
}
// libvlc_media_stats_t ABI from the bundled, pinned LibVLC 3.0.23.
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
int tvlc_audio_only(TVLC *v){
    if(!v)return 0;
    int (*f)(void *)=FN(int (*)(void *),"libvlc_video_get_track_count");
    return f&&f(v->player)==0;
}
void tvlc_pause(TVLC *v,int p){if(v)FN(void (*)(void *,int),"libvlc_media_player_set_pause")(v->player,p);}
int tvlc_can_pause(TVLC *v){return v?FN(int (*)(void *),"libvlc_media_player_can_pause")(v->player):0;}
int tvlc_state(TVLC *v){return v?FN(int (*)(void *),"libvlc_media_player_get_state")(v->player):0;}
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
