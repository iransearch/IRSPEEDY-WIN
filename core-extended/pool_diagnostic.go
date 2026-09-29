package main

import (
	"context"
	"errors"
	"io"
	"net"
	"os"
	"strings"
)

// Return only fixed diagnostic codes. Errors may contain credentials, server
// addresses and destinations; never copy their text into persistent app logs.
func poolErrorReason(err error) string {
	if err == nil {
		return "none"
	}
	message := strings.ToLower(err.Error())
	for _, item := range []struct{ text, code string }{
		{"network changed", "network-changed"},
		{"no recent network activity", "quic-timeout"},
		{"tls:", "tls-error"},
		{"x509:", "certificate-error"},
		{"authentication", "authentication-error"},
		{"certificate", "certificate-error"},
		{"domain resolver not found", "resolver-missing"},
		{"no such host", "dns-not-found"},
		{"dns:", "dns-error"},
		{"network is unreachable", "network-unreachable"},
		{"no route to host", "network-unreachable"},
		{"connection refused", "connection-refused"},
		{"actively refused", "connection-refused"},
		{"connection reset", "connection-reset"},
		{"forcibly closed", "connection-reset"},
		{"operation not supported", "unsupported"},
		{"not implemented", "unsupported"},
		{"udp disabled", "udp-disabled"},
	} {
		if strings.Contains(message, item.text) {
			return item.code
		}
	}
	var dnsErr *net.DNSError
	if errors.As(err, &dnsErr) {
		return "dns-error"
	}
	switch {
	case errors.Is(err, context.Canceled):
		return "canceled"
	case errors.Is(err, context.DeadlineExceeded):
		return "timeout"
	case errors.Is(err, net.ErrClosed):
		return "connection-closed"
	case errors.Is(err, os.ErrInvalid):
		return "invalid-operation"
	case errors.Is(err, io.ErrUnexpectedEOF), errors.Is(err, io.EOF):
		return "eof"
	}
	var netErr net.Error
	if errors.As(err, &netErr) && netErr.Timeout() {
		return "timeout"
	}
	return "other"
}
