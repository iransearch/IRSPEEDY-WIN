package main

import "github.com/golang/protobuf/proto"

type EmptyReq struct {
}

func (m *EmptyReq) Reset()         { *m = EmptyReq{} }
func (m *EmptyReq) String() string { return proto.CompactTextString(m) }
func (*EmptyReq) ProtoMessage()    {}

type EmptyResp struct {
}

func (m *EmptyResp) Reset()         { *m = EmptyResp{} }
func (m *EmptyResp) String() string { return proto.CompactTextString(m) }
func (*EmptyResp) ProtoMessage()    {}

type ErrorResp struct {
	Error string `protobuf:"bytes,1,opt,name=error,proto3" json:"error,omitempty"`
}

func (m *ErrorResp) Reset()         { *m = ErrorResp{} }
func (m *ErrorResp) String() string { return proto.CompactTextString(m) }
func (*ErrorResp) ProtoMessage()    {}

type IsPrivilegedResponse struct {
	HasPrivilege bool `protobuf:"varint,1,opt,name=has_privilege,proto3" json:"has_privilege,omitempty"`
}

func (m *IsPrivilegedResponse) Reset()         { *m = IsPrivilegedResponse{} }
func (m *IsPrivilegedResponse) String() string { return proto.CompactTextString(m) }
func (*IsPrivilegedResponse) ProtoMessage()    {}

type LoadConfigReq struct {
	CoreConfig       string `protobuf:"bytes,1,opt,name=core_config,proto3" json:"core_config,omitempty"`
	NeedExtraProcess bool   `protobuf:"varint,3,opt,name=need_extra_process,proto3" json:"need_extra_process,omitempty"`
	NeedXray         bool   `protobuf:"varint,9,opt,name=need_xray,proto3" json:"need_xray,omitempty"`
	XrayConfig       string `protobuf:"bytes,10,opt,name=xray_config,proto3" json:"xray_config,omitempty"`
}

func (m *LoadConfigReq) Reset()         { *m = LoadConfigReq{} }
func (m *LoadConfigReq) String() string { return proto.CompactTextString(m) }
func (*LoadConfigReq) ProtoMessage()    {}

type TestReq struct {
	Config             string   `protobuf:"bytes,1,opt,name=config,proto3" json:"config,omitempty"`
	OutboundTags       []string `protobuf:"bytes,2,rep,name=outbound_tags,proto3" json:"outbound_tags,omitempty"`
	UseDefaultOutbound bool     `protobuf:"varint,3,opt,name=use_default_outbound,proto3" json:"use_default_outbound,omitempty"`
	Url                string   `protobuf:"bytes,4,opt,name=url,proto3" json:"url,omitempty"`
	TestCurrent        bool     `protobuf:"varint,5,opt,name=test_current,proto3" json:"test_current,omitempty"`
	MaxConcurrency     int32    `protobuf:"varint,6,opt,name=max_concurrency,proto3" json:"max_concurrency,omitempty"`
	TestTimeoutMs      int32    `protobuf:"varint,7,opt,name=test_timeout_ms,proto3" json:"test_timeout_ms,omitempty"`
	NeedXray           bool     `protobuf:"varint,8,opt,name=need_xray,proto3" json:"need_xray,omitempty"`
	XrayConfig         string   `protobuf:"bytes,9,opt,name=xray_config,proto3" json:"xray_config,omitempty"`
}

func (m *TestReq) Reset()         { *m = TestReq{} }
func (m *TestReq) String() string { return proto.CompactTextString(m) }
func (*TestReq) ProtoMessage()    {}

type URLTestResp struct {
	OutboundTag string `protobuf:"bytes,1,opt,name=outbound_tag,proto3" json:"outbound_tag,omitempty"`
	LatencyMs   int32  `protobuf:"varint,2,opt,name=latency_ms,proto3" json:"latency_ms,omitempty"`
	Error       string `protobuf:"bytes,3,opt,name=error,proto3" json:"error,omitempty"`
}

func (m *URLTestResp) Reset()         { *m = URLTestResp{} }
func (m *URLTestResp) String() string { return proto.CompactTextString(m) }
func (*URLTestResp) ProtoMessage()    {}

type TestResp struct {
	Results []*URLTestResp `protobuf:"bytes,1,rep,name=results,proto3" json:"results,omitempty"`
}

func (m *TestResp) Reset()         { *m = TestResp{} }
func (m *TestResp) String() string { return proto.CompactTextString(m) }
func (*TestResp) ProtoMessage()    {}
