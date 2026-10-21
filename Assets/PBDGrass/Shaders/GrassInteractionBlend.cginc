#ifndef GRASS_INTERACTION_BLEND
#define GRASS_INTERACTION_BLEND
float GrassLifecycleTime,GrassLifecycleEntrySeconds,GrassLifecycleExitSeconds;
// State layout is unchanged: 0 stable, 1 entry/legacy exit, 2 live exit,
// negative values encode a resumed entry's initial physical coverage weight.
float GrassPhysicalWeight(int pool,float started,float phase)
{
    if(pool<0)return 0;
    if(phase==2)
    {float t=saturate((GrassLifecycleTime-started)/max(.05,GrassLifecycleExitSeconds));return 1-t*t*(3-2*t);}
    if(phase<0)
    {float t=saturate((GrassLifecycleTime-started)/max(.05,GrassLifecycleEntrySeconds));return lerp(saturate(-phase-1),1,t*t*(3-2*t));}
    return 1;
}
#endif
