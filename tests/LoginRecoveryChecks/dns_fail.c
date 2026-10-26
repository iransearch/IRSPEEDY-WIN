#define _GNU_SOURCE
#include <netdb.h>
#include <dlfcn.h>
#include <string.h>

/* Test-only resolver boundary. Never touch machine DNS or public resolvers. */
int getaddrinfo(const char *name, const char *service, const struct addrinfo *hints, struct addrinfo **result) {
    if (name && strlen(name) >= 8 && strcmp(name + strlen(name) - 8, ".invalid") == 0)
        return EAI_NONAME;
    int (*original)(const char *, const char *, const struct addrinfo *, struct addrinfo **) = dlsym(RTLD_NEXT, "getaddrinfo");
    return original(name, service, hints, result);
}
