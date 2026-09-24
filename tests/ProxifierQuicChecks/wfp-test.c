/* Linux test double, NOT shipped in IRSpeedy. Layouts from Windows SDK fwpmtypes.h.
 * Validates the managed/native ABI, transaction rollback and handle ownership.
 * Does not implement Windows filtering or claim to test network behaviour. */
#include <stdint.h>
#include <stdlib.h>
#include <stddef.h>
#include <string.h>

typedef struct { uint32_t a; uint16_t b, c; uint8_t d[8]; } guid;
typedef struct { void *name, *description; } display;
typedef struct { uint32_t size; void *data; } blob;
typedef struct { uint32_t type; uintptr_t data; } value;
typedef struct { guid field; uint32_t match; value val; } condition;
typedef struct { uint32_t type; guid key; } action;
typedef union { uint64_t raw; guid provider; } context;
typedef struct {
    guid key; display text; uint32_t flags; void *provider; blob data;
    guid layer, sublayer; value weight; uint32_t count; condition *conditions;
    action act; context ctx; void *reserved; uint64_t id; value effective;
} filter;
typedef struct {
    guid key; display text; uint32_t flags, timeout, pid;
    void *sid, *username; int32_t kernel;
} session;

static int opened, closed, added, committed, aborted, failed, invalid;
static const guid app = { 0xd78e1e87,0x8644,0x4ea5,{0x94,0x37,0xd8,0x09,0xec,0xef,0xc9,0x71} };
static const guid proto = { 0x3971ef2b,0x623e,0x4f9a,{0x8c,0xb1,0x6e,0x79,0xb8,0x06,0xb9,0xa7} };
static const guid port = { 0xc35a604d,0xd22b,0x4e1a,{0x91,0xb4,0x68,0xf6,0x74,0xee,0x67,0x4b} };
void Reset(int failAt) { opened=closed=added=committed=aborted=invalid=0; failed=failAt; }
int Count(int which) { int counts[]={opened,closed,added,committed,aborted,invalid}; return counts[which]; }
size_t Layout(int which) { size_t sizes[]={sizeof(session),sizeof(filter),sizeof(condition),offsetof(filter,ctx),offsetof(filter,id)}; return sizes[which]; }
uint32_t FwpmEngineOpen0(void *server, uint32_t auth, void *identity, session *s, void **handle) {
    if(s->flags!=1 || auth!=10 || server || identity) { invalid++; return 87; }
    *handle=malloc(1); opened++; return 0;
}
uint32_t FwpmEngineClose0(void *handle) { free(handle); closed++; return 0; }
uint32_t FwpmGetAppIdFromFileName0(void *name, blob **result) {
    *result=calloc(1,sizeof(blob)); (*result)->size=2; (*result)->data=name; return 0;
}
void FwpmFreeMemory0(void **ptr) { free(*ptr); *ptr=0; }
uint32_t FwpmTransactionBegin0(void *handle, uint32_t flags) { return 0; }
uint32_t FwpmTransactionCommit0(void *handle) { committed++; return 0; }
uint32_t FwpmTransactionAbort0(void *handle) { aborted++; return 0; }
uint32_t FwpmFilterAdd0(void *handle, filter *f, void *security, uint64_t *id) {
    added++;
    if(f->count!=3 || f->flags || f->act.type!=0x1001 || !f->text.name || !f->conditions) goto bad;
    condition *c=f->conditions;
    if(memcmp(&c[0].field,&app,16) || c[0].match || c[0].val.type!=12 || !c[0].val.data) goto bad;
    if(memcmp(&c[1].field,&proto,16) || c[1].match || c[1].val.type!=1 || c[1].val.data!=17) goto bad;
    if(memcmp(&c[2].field,&port,16) || c[2].match || c[2].val.type!=2 || c[2].val.data!=443) goto bad;
    if(f->layer.a!=0xc38d57d1 && f->layer.a!=0x4a72393b) goto bad;
    if(added==failed) return 5;
    *id=added; return 0;
bad:
    invalid++; return 87;
}
