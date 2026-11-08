#include "Scripting.hpp"
#include "Fs.hpp"
#include "World.hpp"
#include <cstring>
#include <set>

namespace mg::scripting {

// ================================================================ Value helpers

bool tryParseDouble(const std::string& in, double& out) {
    size_t b = 0, e = in.size();
    while (b < e && std::isspace((unsigned char)in[b])) b++;
    while (e > b && std::isspace((unsigned char)in[e - 1])) e--;
    if (b == e) return false;
    std::string s = in.substr(b, e - b);
    for (char c : s) if (!(std::isdigit((unsigned char)c) || c == '+' || c == '-' || c == '.' || c == 'e' || c == 'E')) return false;
    char* end = nullptr;
    double d = std::strtod(s.c_str(), &end);
    if (end == s.c_str() || *end != 0) return false;
    out = d;
    return true;
}

std::string formatNumber(double d) {
    if (std::isnan(d)) return "NaN";
    if (std::isinf(d)) return d > 0 ? "Infinity" : "-Infinity";
    char buf[512];
    std::snprintf(buf, sizeof buf, "%.6f", d);
    std::string s = buf;
    if (s.find('.') != std::string::npos) {
        while (!s.empty() && s.back() == '0') s.pop_back();
        if (!s.empty() && s.back() == '.') s.pop_back();
    }
    if (s == "-0" && d == 0 && !std::signbit(d)) s = "0";
    return s;
}

Value Value::parse(const std::string& s) {
    if (s == "true") return True();
    if (s == "false") return False();
    double d;
    if (tryParseDouble(s, d)) return from(d);
    return from(s);
}

// ================================================================ Chunk (binary form + disassembler)

namespace {
struct BinWriter {
    std::vector<uint8_t> b;
    void u8(uint8_t v) { b.push_back(v); }
    void i32(int32_t v) { for (int i = 0; i < 4; i++) b.push_back((uint8_t)((uint32_t)v >> (8 * i))); }
    void f64(double d) { uint8_t t[8]; std::memcpy(t, &d, 8); b.insert(b.end(), t, t + 8); }
    void str(const std::string& s) {
        uint32_t n = (uint32_t)s.size();
        do { uint8_t c = n & 0x7F; n >>= 7; if (n) c |= 0x80; b.push_back(c); } while (n);
        b.insert(b.end(), s.begin(), s.end());
    }
};
struct BinReader {
    const std::vector<uint8_t>& b;
    size_t p = 0;
    explicit BinReader(const std::vector<uint8_t>& d) : b(d) {}
    void need(size_t n) { if (p + n > b.size()) throw ScriptException("truncated bytecode file"); }
    uint8_t u8() { need(1); return b[p++]; }
    int32_t i32() { need(4); uint32_t v = 0; for (int i = 0; i < 4; i++) v |= (uint32_t)b[p + i] << (8 * i); p += 4; return (int32_t)v; }
    double f64() { need(8); double d; std::memcpy(&d, &b[p], 8); p += 8; return d; }
    std::string str() {
        uint32_t n = 0; int shift = 0;
        while (true) { uint8_t c = u8(); n |= (uint32_t)(c & 0x7F) << shift; if (!(c & 0x80)) break; shift += 7; if (shift > 28) throw ScriptException("bad string length"); }
        need(n);
        std::string s((const char*)&b[p], n);
        p += n;
        return s;
    }
};
} // namespace

std::vector<uint8_t> Chunk::write() const {
    BinWriter w;
    for (const char* m = Magic; *m; ++m) w.u8((uint8_t)*m);
    w.i32(Version);
    w.str(name);
    w.i32((int)constants.size());
    for (auto& c : constants) {
        w.u8((uint8_t)c.kind);
        switch (c.kind) {
            case ValueKind::Num: case ValueKind::Bool: w.f64(c.num); break;
            case ValueKind::Str: w.str(c.str); break;
            default: break;
        }
    }
    w.i32((int)natives.size());
    for (auto& n : natives) w.str(n);
    w.i32((int)functions.size());
    for (auto& f : functions) { w.str(f.name); w.i32(f.arity); w.i32(f.locals); w.i32(f.entry); }
    w.i32((int)handlers.size());
    for (auto& h : handlers) { w.str(h.event); w.str(h.target.value_or("")); w.i32(h.function); }
    w.i32(initFunction);
    w.i32((int)code.size());
    w.b.insert(w.b.end(), code.begin(), code.end());
    return w.b;
}

std::shared_ptr<Chunk> Chunk::read(const std::vector<uint8_t>& data) {
    BinReader r(data);
    for (const char* m = Magic; *m; ++m) if (r.u8() != (uint8_t)*m) throw ScriptException("not a script bytecode file");
    if (r.i32() != Version) throw ScriptException("unsupported bytecode version");
    auto c = std::make_shared<Chunk>();
    c->name = r.str();
    int nc = r.i32();
    for (int i = 0; i < nc; i++) {
        auto kind = (ValueKind)r.u8();
        switch (kind) {
            case ValueKind::Num: c->constants.push_back(Value::from(r.f64())); break;
            case ValueKind::Bool: c->constants.push_back(Value::from(r.f64() != 0)); break;
            case ValueKind::Str: c->constants.push_back(Value::from(r.str())); break;
            default: c->constants.push_back(Value::nil()); break;
        }
    }
    int nn = r.i32();
    for (int i = 0; i < nn; i++) c->natives.push_back(r.str());
    int nf = r.i32();
    for (int i = 0; i < nf; i++) {
        FunctionInfo f; f.name = r.str(); f.arity = r.i32(); f.locals = r.i32(); f.entry = r.i32();
        c->functions.push_back(f);
    }
    int nh = r.i32();
    for (int i = 0; i < nh; i++) {
        HandlerInfo h; h.event = r.str(); std::string target = r.str();
        h.function = r.i32();
        if (!target.empty()) h.target = target;
        c->handlers.push_back(h);
    }
    c->initFunction = r.i32();
    int len = r.i32();
    r.need((size_t)len);
    c->code.assign(data.begin() + r.p, data.begin() + r.p + len);
    return c;
}

std::string Chunk::disassemble() const {
    std::string sb;
    char buf[256];
    std::snprintf(buf, sizeof buf, "; %s  %d bytes, %d constants, %d functions\n", name.c_str(), (int)code.size(), (int)constants.size(), (int)functions.size());
    sb += buf;
    static const char* names[] = {"Nop","Const","Nil","True","False","Pop","GetLocal","SetLocal","GetGlobal","SetGlobal","Add","Sub","Mul","Div","Mod","Neg","Not","Eq","Ne","Lt","Le","Gt","Ge","Jump","JumpIfFalse","JumpIfFalseKeep","JumpIfTrueKeep","Call","CallNative","Return"};
    auto u16 = [&](int& ip) { int v = code[ip] | (code[ip + 1] << 8); ip += 2; return v; };
    auto quote = [](const Value& v) { return v.kind == ValueKind::Str ? "\"" + v.str + "\"" : v.asString(); };
    for (size_t fi = 0; fi < functions.size(); fi++) {
        auto& f = functions[fi];
        std::snprintf(buf, sizeof buf, "\nfunc #%d %s  (arity %d, locals %d)%s\n", (int)fi, f.name.c_str(), f.arity, f.locals, (int)fi == initFunction ? "  ; init" : "");
        sb += buf;
        int end = (int)code.size();
        for (auto& o : functions) if (o.entry > f.entry && o.entry < end) end = o.entry;
        int ip = f.entry;
        while (ip < end) {
            int at = ip;
            auto op = (OpCode)code[ip++];
            std::string operand;
            switch (op) {
                case OpCode::Const: { int k = u16(ip); operand = std::to_string(k) + "  ; " + quote(constants[k]); break; }
                case OpCode::GetGlobal: case OpCode::SetGlobal: { int k = u16(ip); operand = std::to_string(k) + "  ; " + constants[k].asString(); break; }
                case OpCode::GetLocal: case OpCode::SetLocal: operand = std::to_string((int)code[ip++]); break;
                case OpCode::Jump: case OpCode::JumpIfFalse: case OpCode::JumpIfFalseKeep: case OpCode::JumpIfTrueKeep: {
                    int off = (short)u16(ip);
                    std::snprintf(buf, sizeof buf, "%+d  ; -> %04d", off, ip + off);
                    operand = buf; break; }
                case OpCode::Call: { int k = u16(ip); int argc = code[ip++]; operand = functions[k].name + ", " + std::to_string(argc) + " args"; break; }
                case OpCode::CallNative: { int k = u16(ip); int argc = code[ip++]; operand = this->natives[k] + ", " + std::to_string(argc) + " args"; break; }
                default: break;
            }
            std::snprintf(buf, sizeof buf, "  %04d  %-16s %s\n", at, names[(int)op], operand.c_str());
            sb += buf;
        }
    }
    sb += "\nhandlers:\n";
    for (auto& h : handlers)
        sb += "  on " + h.event + (h.target && !h.target->empty() ? " \"" + *h.target + "\"" : "") + " -> " + functions[h.function].name + "\n";
    return sb;
}

// ================================================================ Lexer

static bool isLetter(char c) { return std::isalpha((unsigned char)c) || (unsigned char)c >= 0x80; }
static bool isDigit(char c) { return c >= '0' && c <= '9'; }

std::vector<Token> tokenize(const std::string& src) {
    std::vector<Token> list;
    size_t i = 0;
    int line = 1;
    while (i < src.size()) {
        char c = src[i];
        if (c == '\n') { line++; i++; continue; }
        if (std::isspace((unsigned char)c)) { i++; continue; }
        if (c == '/' && i + 1 < src.size() && src[i + 1] == '/') {
            while (i < src.size() && src[i] != '\n') i++;
            continue;
        }
        if (c == '/' && i + 1 < src.size() && src[i + 1] == '*') {
            i += 2;
            while (i + 1 < src.size() && !(src[i] == '*' && src[i + 1] == '/')) { if (src[i] == '\n') line++; i++; }
            i = std::min(src.size(), i + 2);
            continue;
        }
        if (isDigit(c) || (c == '.' && i + 1 < src.size() && isDigit(src[i + 1]))) {
            size_t s = i;
            while (i < src.size() && (isDigit(src[i]) || src[i] == '.')) i++;
            std::string t = src.substr(s, i - s);
            double d;
            if (!tryParseDouble(t, d)) throw ScriptException("bad number '" + t + "'", line);
            list.push_back({Tok::Number, t, d, line});
            continue;
        }
        if (isLetter(c) || c == '_') {
            size_t s = i;
            while (i < src.size() && (isLetter(src[i]) || isDigit(src[i]) || src[i] == '_')) i++;
            list.push_back({Tok::Ident, src.substr(s, i - s), 0, line});
            continue;
        }
        if (c == '"') {
            std::string sb;
            i++;
            while (i < src.size() && src[i] != '"') {
                if (src[i] == '\n') throw ScriptException("unterminated string", line);
                if (src[i] == '\\' && i + 1 < src.size()) {
                    i++;
                    char e = src[i];
                    sb += (e == 'n') ? '\n' : (e == 't') ? '\t' : e;
                    i++;
                    continue;
                }
                sb += src[i++];
            }
            if (i >= src.size()) throw ScriptException("unterminated string", line);
            i++;
            list.push_back({Tok::String, sb, 0, line});
            continue;
        }

        char n = i + 1 < src.size() ? src[i + 1] : '\0';
        Tok kind = Tok::Eof;
        size_t len = 1;
        switch (c) {
            case '(': kind = Tok::LParen; break;
            case ')': kind = Tok::RParen; break;
            case '{': kind = Tok::LBrace; break;
            case '}': kind = Tok::RBrace; break;
            case ',': kind = Tok::Comma; break;
            case ';': kind = Tok::Semi; break;
            case '*': kind = Tok::Star; break;
            case '/': kind = Tok::Slash; break;
            case '%': kind = Tok::Percent; break;
            case '+': if (n == '=') { kind = Tok::PlusEq; len = 2; } else kind = Tok::Plus; break;
            case '-': if (n == '=') { kind = Tok::MinusEq; len = 2; } else kind = Tok::Minus; break;
            case '!': if (n == '=') { kind = Tok::Ne; len = 2; } else kind = Tok::Bang; break;
            case '=': if (n == '=') { kind = Tok::Eq; len = 2; } else kind = Tok::Assign; break;
            case '<': if (n == '=') { kind = Tok::Le; len = 2; } else kind = Tok::Lt; break;
            case '>': if (n == '=') { kind = Tok::Ge; len = 2; } else kind = Tok::Gt; break;
            case '&': if (n == '&') { kind = Tok::AndAnd; len = 2; } else throw ScriptException("unexpected '&' (use &&)", line); break;
            case '|': if (n == '|') { kind = Tok::OrOr; len = 2; } else throw ScriptException("unexpected '|' (use ||)", line); break;
            default: throw ScriptException(std::string("unexpected character '") + c + "'", line);
        }
        list.push_back({kind, src.substr(i, len), 0, line});
        i += len;
    }
    list.push_back({Tok::Eof, "", 0, line});
    return list;
}

// ================================================================ Compiler

namespace {

const std::set<std::string> Events = {"start", "tick", "trigger", "enter", "exit", "use", "death", "win", "gem", "touch", "remove"};
const std::set<std::string> Keywords = {"var", "func", "on", "if", "else", "while", "return", "break", "continue", "true", "false", "nil"};

struct Local { std::string name; int depth = 0; bool active = true; };
struct Loop { std::vector<int> breaks, continues; int start = 0; int scopeDepth = 0; };
struct FuncBuilder {
    std::string name;
    int arity = 0;
    int funcIndex = 0;
    std::vector<uint8_t> code;
    std::vector<Local> locals;
    std::vector<Loop> loops;
    int depth = 0;
    bool isInit = false;
};

class ScriptCompiler {
public:
    ScriptCompiler(const std::string& source, const std::string& name) : name_(name), t_(tokenize(source)) {}

    std::shared_ptr<Chunk> run();

private:
    std::vector<Token> t_;
    size_t p_ = 0;
    std::string name_;
    std::shared_ptr<Chunk> chunk_ = std::make_shared<Chunk>();
    std::vector<uint8_t> code_;
    std::vector<Value> consts_;
    std::unordered_map<std::string, int> constIndex_;
    std::unordered_map<std::string, int> funcIndex_;
    std::set<std::string> globals_;
    struct Pending { std::string name; int argc; int line; };
    std::vector<Pending> pendingCalls_;
    FuncBuilder* f_ = nullptr;

    int funcSlot(const std::string& name);
    void finish(FuncBuilder& f, int locals);
    void functionDecl();
    void handlerDecl();
    void block();
    void beginScope() { f_->depth++; }
    void endScope();
    void statement();
    void varStatement();
    void assignment(const std::string& name, Tok op, int line);
    void ifStatement();
    void whileStatement();
    void expression() { orExpr(); }
    void orExpr();
    void andExpr();
    void equality();
    void comparison();
    void term();
    void factor();
    void unary();
    void primary();
    void callExpr(const Token& name);
    int addLocal(const std::string& name);
    int findLocal(const std::string& name);
    void getVariable(const std::string& name, int line);
    void setVariable(const std::string& name, int line);

    void emit(OpCode op) { f_->code.push_back((uint8_t)op); }
    void emitB(int b) { f_->code.push_back((uint8_t)b); }
    void emitU16(int v);
    int constant(const std::string& s) { return constant(Value::from(s)); }
    int constant(const Value& v);
    int emitJump(OpCode op) { emit(op); emitB(0); emitB(0); return (int)f_->code.size() - 2; }
    void patch(int pos) { patchTo(pos, (int)f_->code.size()); }
    void patchTo(int pos, int target);
    void emitLoop(int target) { emit(OpCode::Jump); emitB(0); emitB(0); patchTo((int)f_->code.size() - 2, target); }

    const Token& peek() const { return t_[p_]; }
    Token next() { return t_[p_ < t_.size() - 1 ? p_++ : p_]; }
    bool match(Tok k) { if (peek().kind == k) { next(); return true; } return false; }
    bool isWord(const char* w) const { return peek().kind == Tok::Ident && peek().text == w; }
    ScriptException err(const std::string& msg) const { return ScriptException(msg, peek().line); }
    void expect(Tok k, const char* what) {
        if (peek().kind != k) throw err(std::string("expected ") + what + " but found '" + (peek().kind == Tok::Eof ? "end of file" : peek().text) + "'");
        next();
    }
    std::string expectIdent(const char* what) {
        if (peek().kind != Tok::Ident || Keywords.count(peek().text)) throw err(std::string("expected ") + what);
        return next().text;
    }
};

std::shared_ptr<Chunk> ScriptCompiler::run() {
    chunk_->name = name_;
    // pre-scan: top-level "var name" declarations are globals, so functions may use them before the declaration
    int depth = 0;
    for (size_t i = 0; i < t_.size(); i++) {
        auto& tk = t_[i];
        if (tk.kind == Tok::LBrace) depth++;
        else if (tk.kind == Tok::RBrace) depth--;
        else if (depth == 0 && tk.kind == Tok::Ident && tk.text == "var" && i + 1 < t_.size() && t_[i + 1].kind == Tok::Ident)
            globals_.insert(t_[i + 1].text);
    }

    FuncBuilder init;
    init.name = "__init"; init.isInit = true; init.funcIndex = funcSlot("__init");
    chunk_->initFunction = init.funcIndex;

    while (peek().kind != Tok::Eof) {
        if (isWord("var")) {
            f_ = &init;
            next();
            std::string name = expectIdent("variable name");
            if (match(Tok::Assign)) expression(); else emit(OpCode::Nil);
            expect(Tok::Semi, "';'");
            emit(OpCode::SetGlobal); emitU16(constant(name));
        } else if (isWord("func")) functionDecl();
        else if (isWord("on")) handlerDecl();
        else throw err("expected 'var', 'func' or 'on' but found '" + peek().text + "'");
    }

    f_ = &init;
    emit(OpCode::Nil); emit(OpCode::Return);
    finish(init, (int)init.locals.size());

    for (auto& pc : pendingCalls_) {
        auto& info = chunk_->functions[funcIndex_[pc.name]];
        if (info.entry < 0) throw ScriptException("call to undefined function '" + pc.name + "'", pc.line);
        if (info.arity != pc.argc) throw ScriptException("function '" + pc.name + "' takes " + std::to_string(info.arity) + " arguments, not " + std::to_string(pc.argc), pc.line);
    }
    chunk_->code = code_;
    chunk_->constants = consts_;
    return chunk_;
}

int ScriptCompiler::funcSlot(const std::string& name) {
    auto it = funcIndex_.find(name);
    if (it != funcIndex_.end()) return it->second;
    int i = (int)chunk_->functions.size();
    FunctionInfo fi; fi.name = name;
    chunk_->functions.push_back(fi);
    funcIndex_[name] = i;
    return i;
}

void ScriptCompiler::finish(FuncBuilder& f, int locals) {
    auto& info = chunk_->functions[f.funcIndex];
    info.arity = f.arity;
    info.locals = std::max(locals, f.arity);
    info.entry = (int)code_.size();
    code_.insert(code_.end(), f.code.begin(), f.code.end());
}

void ScriptCompiler::functionDecl() {
    next();   // func
    std::string name = expectIdent("function name");
    if (ScriptNatives::find(name) != nullptr) throw err("'" + name + "' is a built-in function");
    int slot = funcSlot(name);
    if (chunk_->functions[slot].entry >= 0) throw err("function '" + name + "' is defined twice");

    FuncBuilder f;
    f.name = name; f.funcIndex = slot;
    f_ = &f;
    expect(Tok::LParen, "'('");
    if (peek().kind != Tok::RParen) {
        do { addLocal(expectIdent("parameter name")); f.arity++; } while (match(Tok::Comma));
    }
    expect(Tok::RParen, "')'");
    block();
    emit(OpCode::Nil); emit(OpCode::Return);
    finish(f, (int)f.locals.size());
}

void ScriptCompiler::handlerDecl() {
    next();   // on
    std::string ev = expectIdent("event name");
    if (!Events.count(ev)) {
        std::string list;
        for (auto& e : Events) { if (!list.empty()) list += ", "; list += e; }
        throw err("unknown event '" + ev + "' (use " + list + ")");
    }
    std::optional<std::string> target;
    if (peek().kind == Tok::String) target = next().text;

    std::string tgt = target ? " \"" + *target + "\"" : "";
    std::string fname = "on " + ev + tgt + " #" + std::to_string(chunk_->handlers.size());
    int slot = funcSlot(fname);
    FuncBuilder f;
    f.name = fname; f.funcIndex = slot;
    f_ = &f;
    block();
    emit(OpCode::Nil); emit(OpCode::Return);
    finish(f, (int)f.locals.size());
    chunk_->handlers.push_back({ev, target, slot});
}

void ScriptCompiler::block() {
    expect(Tok::LBrace, "'{'");
    beginScope();
    while (peek().kind != Tok::RBrace) {
        if (peek().kind == Tok::Eof) throw err("missing '}'");
        statement();
    }
    next();
    endScope();
}

void ScriptCompiler::endScope() {
    for (auto& l : f_->locals) if (l.active && l.depth >= f_->depth) l.active = false;
    f_->depth--;
}

void ScriptCompiler::statement() {
    Token t = peek();
    if (t.kind == Tok::LBrace) { block(); return; }
    if (t.kind == Tok::Semi) { next(); return; }
    if (t.kind == Tok::Ident) {
        if (t.text == "var") { varStatement(); return; }
        if (t.text == "if") { ifStatement(); return; }
        if (t.text == "while") { whileStatement(); return; }
        if (t.text == "return") {
            next();
            if (peek().kind == Tok::Semi) emit(OpCode::Nil); else expression();
            expect(Tok::Semi, "';'");
            emit(OpCode::Return);
            return;
        }
        if (t.text == "break") {
            next(); expect(Tok::Semi, "';'");
            if (f_->loops.empty()) throw err("'break' outside a loop");
            f_->loops.back().breaks.push_back(emitJump(OpCode::Jump));
            return;
        }
        if (t.text == "continue") {
            next(); expect(Tok::Semi, "';'");
            if (f_->loops.empty()) throw err("'continue' outside a loop");
            f_->loops.back().continues.push_back(emitJump(OpCode::Jump));
            return;
        }
        // assignment?
        Tok n = t_[p_ + 1].kind;
        if (!Keywords.count(t.text) && (n == Tok::Assign || n == Tok::PlusEq || n == Tok::MinusEq)) {
            next(); next();
            assignment(t.text, n, t.line);
            return;
        }
    }
    expression();
    expect(Tok::Semi, "';'");
    emit(OpCode::Pop);
}

void ScriptCompiler::varStatement() {
    next();
    std::string name = expectIdent("variable name");
    if (f_->isInit && f_->depth == 0) throw err("internal: top-level var");
    // the initialiser is compiled before the name exists, so "var x = x" refers to an outer x
    if (match(Tok::Assign)) expression(); else emit(OpCode::Nil);
    expect(Tok::Semi, "';'");
    int slot = addLocal(name);
    emit(OpCode::SetLocal); emitB(slot);
}

void ScriptCompiler::assignment(const std::string& name, Tok op, int line) {
    if (op != Tok::Assign) getVariable(name, line);
    expression();
    if (op == Tok::PlusEq) emit(OpCode::Add);
    else if (op == Tok::MinusEq) emit(OpCode::Sub);
    expect(Tok::Semi, "';'");
    setVariable(name, line);
}

void ScriptCompiler::ifStatement() {
    next();
    expect(Tok::LParen, "'('");
    expression();
    expect(Tok::RParen, "')'");
    int elseJump = emitJump(OpCode::JumpIfFalse);
    block();
    if (isWord("else")) {
        int endJump = emitJump(OpCode::Jump);
        patch(elseJump);
        next();
        if (isWord("if")) ifStatement(); else block();
        patch(endJump);
    } else patch(elseJump);
}

void ScriptCompiler::whileStatement() {
    next();
    Loop loop;
    loop.start = (int)f_->code.size();
    loop.scopeDepth = f_->depth;
    f_->loops.push_back(loop);
    expect(Tok::LParen, "'('");
    expression();
    expect(Tok::RParen, "')'");
    int exit = emitJump(OpCode::JumpIfFalse);
    block();
    Loop& lp = f_->loops.back();
    for (int c : lp.continues) patchTo(c, lp.start);
    emitLoop(lp.start);
    patch(exit);
    for (int b : f_->loops.back().breaks) patch(b);
    f_->loops.pop_back();
}

void ScriptCompiler::orExpr() {
    andExpr();
    while (match(Tok::OrOr)) {
        int j = emitJump(OpCode::JumpIfTrueKeep);
        andExpr();
        patch(j);
    }
}

void ScriptCompiler::andExpr() {
    equality();
    while (match(Tok::AndAnd)) {
        int j = emitJump(OpCode::JumpIfFalseKeep);
        equality();
        patch(j);
    }
}

void ScriptCompiler::equality() {
    comparison();
    while (true) {
        if (match(Tok::Eq)) { comparison(); emit(OpCode::Eq); }
        else if (match(Tok::Ne)) { comparison(); emit(OpCode::Ne); }
        else break;
    }
}

void ScriptCompiler::comparison() {
    term();
    while (true) {
        if (match(Tok::Lt)) { term(); emit(OpCode::Lt); }
        else if (match(Tok::Le)) { term(); emit(OpCode::Le); }
        else if (match(Tok::Gt)) { term(); emit(OpCode::Gt); }
        else if (match(Tok::Ge)) { term(); emit(OpCode::Ge); }
        else break;
    }
}

void ScriptCompiler::term() {
    factor();
    while (true) {
        if (match(Tok::Plus)) { factor(); emit(OpCode::Add); }
        else if (match(Tok::Minus)) { factor(); emit(OpCode::Sub); }
        else break;
    }
}

void ScriptCompiler::factor() {
    unary();
    while (true) {
        if (match(Tok::Star)) { unary(); emit(OpCode::Mul); }
        else if (match(Tok::Slash)) { unary(); emit(OpCode::Div); }
        else if (match(Tok::Percent)) { unary(); emit(OpCode::Mod); }
        else break;
    }
}

void ScriptCompiler::unary() {
    if (match(Tok::Bang)) { unary(); emit(OpCode::Not); }
    else if (match(Tok::Minus)) { unary(); emit(OpCode::Neg); }
    else primary();
}

void ScriptCompiler::primary() {
    Token t = next();
    switch (t.kind) {
        case Tok::Number: emit(OpCode::Const); emitU16(constant(Value::from(t.num))); return;
        case Tok::String: emit(OpCode::Const); emitU16(constant(Value::from(t.text))); return;
        case Tok::LParen: expression(); expect(Tok::RParen, "')'"); return;
        case Tok::Ident:
            if (t.text == "true") { emit(OpCode::True); return; }
            if (t.text == "false") { emit(OpCode::False); return; }
            if (t.text == "nil") { emit(OpCode::Nil); return; }
            if (Keywords.count(t.text)) throw ScriptException("unexpected '" + t.text + "'", t.line);
            if (peek().kind == Tok::LParen) callExpr(t); else getVariable(t.text, t.line);
            return;
        default: break;
    }
    throw ScriptException("unexpected '" + t.text + "'", t.line);
}

void ScriptCompiler::callExpr(const Token& name) {
    next();   // (
    int argc = 0;
    if (peek().kind != Tok::RParen) {
        do { expression(); argc++; } while (match(Tok::Comma));
    }
    expect(Tok::RParen, "')'");
    if (argc > 255) throw ScriptException("too many arguments", name.line);

    auto* native = ScriptNatives::find(name.text);
    if (native) {
        if (native->arity >= 0 && native->arity != argc)
            throw ScriptException("'" + name.text + "' takes " + std::to_string(native->arity) + " arguments, not " + std::to_string(argc), name.line);
        int idx = -1;
        for (size_t i = 0; i < chunk_->natives.size(); i++) if (chunk_->natives[i] == name.text) { idx = (int)i; break; }
        if (idx < 0) { idx = (int)chunk_->natives.size(); chunk_->natives.push_back(name.text); }
        emit(OpCode::CallNative); emitU16(idx); emitB(argc);
    } else {
        int slot = funcSlot(name.text);
        pendingCalls_.push_back({name.text, argc, name.line});
        emit(OpCode::Call); emitU16(slot); emitB(argc);
    }
}

int ScriptCompiler::addLocal(const std::string& name) {
    for (auto& l : f_->locals)
        if (l.active && l.depth == f_->depth && l.name == name && f_->depth > 0)
            throw err("variable '" + name + "' is already declared in this block");
    if (f_->locals.size() >= 250) throw err("too many local variables");
    f_->locals.push_back({name, f_->depth, true});
    return (int)f_->locals.size() - 1;
}

int ScriptCompiler::findLocal(const std::string& name) {
    for (int i = (int)f_->locals.size() - 1; i >= 0; i--)
        if (f_->locals[i].active && f_->locals[i].name == name) return i;
    return -1;
}

void ScriptCompiler::getVariable(const std::string& name, int line) {
    int slot = findLocal(name);
    if (slot >= 0) { emit(OpCode::GetLocal); emitB(slot); }
    else if (globals_.count(name)) { emit(OpCode::GetGlobal); emitU16(constant(name)); }
    else throw ScriptException("unknown variable '" + name + "'", line);
}

void ScriptCompiler::setVariable(const std::string& name, int line) {
    int slot = findLocal(name);
    if (slot >= 0) { emit(OpCode::SetLocal); emitB(slot); }
    else if (globals_.count(name)) { emit(OpCode::SetGlobal); emitU16(constant(name)); }
    else throw ScriptException("unknown variable '" + name + "' (declare it with var)", line);
}

void ScriptCompiler::emitU16(int v) {
    if (v < 0 || v > 0xFFFF) throw err("too many constants / functions");
    f_->code.push_back((uint8_t)(v & 0xFF));
    f_->code.push_back((uint8_t)(v >> 8));
}

int ScriptCompiler::constant(const Value& v) {
    std::string key = std::to_string((int)v.kind) + ":" + v.asString();
    auto it = constIndex_.find(key);
    if (it != constIndex_.end()) return it->second;
    int i = (int)consts_.size();
    consts_.push_back(v);
    constIndex_[key] = i;
    return i;
}

void ScriptCompiler::patchTo(int pos, int target) {
    int off = target - (pos + 2);
    if (off < -32768 || off > 32767) throw err("jump too far");
    f_->code[pos] = (uint8_t)(off & 0xFF);
    f_->code[pos + 1] = (uint8_t)((off >> 8) & 0xFF);
}

} // namespace

std::shared_ptr<Chunk> compileScript(const std::string& source, const std::string& name) {
    try { return ScriptCompiler(source, name).run(); }
    catch (const ScriptException& ex) { throw ScriptException(name + ": " + ex.what()); }
}

// ================================================================ VM

ScriptTask::ScriptTask(std::shared_ptr<Chunk> chunk, int function, const std::vector<Value>* args, std::string nm)
    : name(std::move(nm)), c_(std::move(chunk)) {
    static const std::vector<Value> none;
    push(function, args ? *args : none);
}

void ScriptTask::push(int func, const std::vector<Value>& args) {
    if (fp_ >= 64) throw ScriptException("call stack overflow");
    auto& info = c_->functions[func];
    int b = sp_;
    for (int i = 0; i < info.locals; i++) stack_[sp_++] = i < (int)args.size() ? args[i] : Value::nil();
    frames_[fp_++] = Frame{func, info.entry, b};
}

void ScriptTask::run(ScriptContext& ctx, std::map<std::string, Value>& globals) {
    if (done_) return;
    if (waitTicks > 0) { waitTicks--; if (waitTicks > 0) return; }
    if (waitDialog) return;
    ctx.task = this;
    const auto& code = c_->code;
    int budget = 20000;

    auto u16 = [&](int& ip) { int v = code[ip] | (code[ip + 1] << 8); ip += 2; return v; };
    auto i16 = [&](int& ip) { int v = (short)(code[ip] | (code[ip + 1] << 8)); ip += 2; return v; };

    while (budget-- > 0) {
        Frame& fr = frames_[fp_ - 1];
        auto op = (OpCode)code[fr.ip++];
        switch (op) {
            case OpCode::Nop: break;
            case OpCode::Const: stack_[sp_++] = c_->constants[u16(fr.ip)]; break;
            case OpCode::Nil: stack_[sp_++] = Value::nil(); break;
            case OpCode::True: stack_[sp_++] = Value::True(); break;
            case OpCode::False: stack_[sp_++] = Value::False(); break;
            case OpCode::Pop: sp_--; break;
            case OpCode::GetLocal: stack_[sp_++] = stack_[fr.base + code[fr.ip++]]; break;
            case OpCode::SetLocal: { int slot = code[fr.ip++]; stack_[fr.base + slot] = stack_[--sp_]; break; }
            case OpCode::GetGlobal: {
                std::string n = c_->constants[u16(fr.ip)].asString();
                auto it = globals.find(n);
                stack_[sp_++] = it != globals.end() ? it->second : Value::nil();
                break;
            }
            case OpCode::SetGlobal: {
                std::string n = c_->constants[u16(fr.ip)].asString();
                globals[n] = stack_[--sp_];
                break;
            }
            case OpCode::Add: {
                Value b = stack_[--sp_]; Value& a = stack_[sp_ - 1];
                if (a.kind == ValueKind::Str || b.kind == ValueKind::Str) a = Value::from(a.asString() + b.asString());
                else a = Value::from(a.asNumber() + b.asNumber());
                break;
            }
            case OpCode::Sub: { Value b = stack_[--sp_]; stack_[sp_ - 1] = Value::from(stack_[sp_ - 1].asNumber() - b.asNumber()); break; }
            case OpCode::Mul: { Value b = stack_[--sp_]; stack_[sp_ - 1] = Value::from(stack_[sp_ - 1].asNumber() * b.asNumber()); break; }
            case OpCode::Div: {
                Value b = stack_[--sp_]; double d = b.asNumber();
                stack_[sp_ - 1] = Value::from(d == 0 ? 0.0 : stack_[sp_ - 1].asNumber() / d);
                break;
            }
            case OpCode::Mod: {
                Value b = stack_[--sp_]; double d = b.asNumber();
                stack_[sp_ - 1] = Value::from(d == 0 ? 0.0 : std::fmod(stack_[sp_ - 1].asNumber(), d));
                break;
            }
            case OpCode::Neg: stack_[sp_ - 1] = Value::from(-stack_[sp_ - 1].asNumber()); break;
            case OpCode::Not: stack_[sp_ - 1] = Value::from(!stack_[sp_ - 1].truthy()); break;
            case OpCode::Eq: { Value b = stack_[--sp_]; stack_[sp_ - 1] = Value::from(stack_[sp_ - 1].equals(b)); break; }
            case OpCode::Ne: { Value b = stack_[--sp_]; stack_[sp_ - 1] = Value::from(!stack_[sp_ - 1].equals(b)); break; }
            case OpCode::Lt: { Value b = stack_[--sp_]; stack_[sp_ - 1] = Value::from(stack_[sp_ - 1].asNumber() < b.asNumber()); break; }
            case OpCode::Le: { Value b = stack_[--sp_]; stack_[sp_ - 1] = Value::from(stack_[sp_ - 1].asNumber() <= b.asNumber()); break; }
            case OpCode::Gt: { Value b = stack_[--sp_]; stack_[sp_ - 1] = Value::from(stack_[sp_ - 1].asNumber() > b.asNumber()); break; }
            case OpCode::Ge: { Value b = stack_[--sp_]; stack_[sp_ - 1] = Value::from(stack_[sp_ - 1].asNumber() >= b.asNumber()); break; }

            case OpCode::Jump: { int o = i16(fr.ip); fr.ip += o; break; }
            case OpCode::JumpIfFalse: { int o = i16(fr.ip); if (!stack_[--sp_].truthy()) fr.ip += o; break; }
            case OpCode::JumpIfFalseKeep: { int o = i16(fr.ip); if (!stack_[sp_ - 1].truthy()) fr.ip += o; else sp_--; break; }
            case OpCode::JumpIfTrueKeep: { int o = i16(fr.ip); if (stack_[sp_ - 1].truthy()) fr.ip += o; else sp_--; break; }

            case OpCode::Call: {
                int f = u16(fr.ip); int argc = code[fr.ip++];
                std::vector<Value> args((size_t)argc);
                for (int i = argc - 1; i >= 0; i--) args[i] = stack_[--sp_];
                push(f, args);
                break;
            }
            case OpCode::CallNative: {
                int n = u16(fr.ip); int argc = code[fr.ip++];
                std::vector<Value> args((size_t)argc);
                for (int i = argc - 1; i >= 0; i--) args[i] = stack_[--sp_];
                auto* native = ScriptNatives::find(c_->natives[n]);
                stack_[sp_++] = native == nullptr ? Value::nil() : native->fn(ctx, args);
                if (waitTicks > 0 || waitDialog) return;
                break;
            }
            case OpCode::Return: {
                Value rv = stack_[--sp_];
                sp_ = fr.base;
                fp_--;
                if (fp_ == 0) { done_ = true; return; }
                stack_[sp_++] = rv;
                break;
            }
            default: throw ScriptException("bad opcode " + std::to_string((int)op) + " in " + name);
        }
    }
    std::printf("[script] '%s' ran too long in one tick; yielding\n", name.c_str());
}

// ================================================================ Host + library

ScriptHost::ScriptHost(World* world, std::shared_ptr<Chunk> chunk, Entity* self, bool isPlayer, bool runInit)
    : chunk_(std::move(chunk)), world_(world) {
    ctx_.world = world; ctx_.host = this; ctx_.self = self; ctx_.isPlayer = isPlayer;
    if (runInit) {
        if (chunk_->initFunction >= 0) spawn(chunk_->initFunction, "init");
        runTasks();
    }
}

bool ScriptHost::hasHandler(const std::string& ev) const {
    for (auto& h : chunk_->handlers) if (h.event == ev) return true;
    return false;
}

void ScriptHost::restoreGlobals(const std::map<std::string, Value>* g) {
    tasks_.clear();
    globals_.clear();
    if (g) for (auto& kv : *g) globals_[kv.first] = kv.second;
}

void ScriptHost::spawn(int func, const std::string& name) {
    tasks_.push_back(std::make_unique<ScriptTask>(chunk_, func, nullptr, name));
}

void ScriptHost::fire(const std::string& ev, const std::optional<std::string>& target) {
    for (auto& h : chunk_->handlers) {
        if (h.event != ev) continue;
        if (h.target && !h.target->empty() && (!target || *h.target != *target)) continue;
        if (tasks_.size() > 64) return;
        spawn(h.function, "on " + ev + " " + target.value_or(""));
    }
}

void ScriptHost::dialogClosed() {
    for (auto& t : tasks_) t->waitDialog = false;
}

void ScriptHost::tick() {
    fire("tick");
    runTasks();
}

void ScriptHost::runTasks() {
    for (size_t i = 0; i < tasks_.size(); i++) {
        try { tasks_[i]->run(ctx_, globals_); }
        catch (const std::exception& ex) {
            std::printf("[script] error in %s: %s\n", chunk_->name.c_str(), ex.what());
            tasks_.erase(tasks_.begin() + (long)i);
            i--;
            continue;
        }
        if (tasks_[i]->done()) { tasks_.erase(tasks_.begin() + (long)i); i--; }
    }
}

namespace {
std::map<std::string, std::shared_ptr<Chunk>>& cache() { static std::map<std::string, std::shared_ptr<Chunk>> c; return c; }
}

std::shared_ptr<Chunk> ScriptLibrary::get(const std::string& id) {
    if (id.empty()) return nullptr;
    auto it = cache().find(id);
    if (it != cache().end()) return it->second;
    std::shared_ptr<Chunk> c;
    try {
        std::string dir = fs::join(baseDir(), "scripts");
        std::string mgb = fs::join(dir, id + ".mgb"), mgs = fs::join(dir, id + ".mgs");
        if (fs::fileExists(mgb)) {
            std::vector<uint8_t> bytes;
            fs::readFileBytes(mgb, bytes);
            c = Chunk::read(bytes);
        } else if (fs::fileExists(mgs)) {
            std::string text;
            fs::readFile(mgs, text);
            c = compileScript(text, id);
        }
    } catch (const std::exception& ex) {
        std::printf("[script] failed to load '%s': %s\n", id.c_str(), ex.what());
    }
    cache()[id] = c;
    return c;
}

void ScriptLibrary::clearCache() { cache().clear(); }

} // namespace mg::scripting
