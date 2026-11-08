#pragma once
// MazeGame.Core.Scripting: values, bytecode chunk, lexer, compiler, VM, host and natives.
#include "Common.hpp"
#include <stdexcept>

namespace mg {
class World;
class Entity;
}

namespace mg::scripting {

class ScriptException : public std::runtime_error {
public:
    int line;
    explicit ScriptException(const std::string& msg, int ln = 0)
        : std::runtime_error(ln > 0 ? "line " + std::to_string(ln) + ": " + msg : msg), line(ln) {}
};

// ---------------------------------------------------------------- Value
enum class ValueKind : uint8_t { Nil, Bool, Num, Str };

bool tryParseDouble(const std::string& s, double& out);
std::string formatNumber(double d);   // C# "0.######" invariant

struct Value {
    ValueKind kind = ValueKind::Nil;
    double num = 0;
    std::string str;

    static Value nil() { return Value(); }
    static Value True() { Value v; v.kind = ValueKind::Bool; v.num = 1; return v; }
    static Value False() { Value v; v.kind = ValueKind::Bool; v.num = 0; return v; }
    static Value from(bool b) { return b ? True() : False(); }
    static Value from(double d) { Value v; v.kind = ValueKind::Num; v.num = d; return v; }
    static Value from(int d) { return from((double)d); }
    static Value from(const std::string& s) { Value v; v.kind = ValueKind::Str; v.str = s; return v; }
    static Value from(const char* s) { return from(std::string(s)); }

    bool isNil() const { return kind == ValueKind::Nil; }
    bool truthy() const {
        switch (kind) {
            case ValueKind::Nil: return false;
            case ValueKind::Bool: case ValueKind::Num: return num != 0;
            default: return !str.empty();
        }
    }
    double asNumber(double fallback = 0) const {
        switch (kind) {
            case ValueKind::Num: case ValueKind::Bool: return num;
            case ValueKind::Str: { double d; return tryParseDouble(str, d) ? d : fallback; }
            default: return fallback;
        }
    }
    int asInt(int fallback = 0) const { return (int)std::floor(asNumber(fallback)); }
    std::string asString() const {
        switch (kind) {
            case ValueKind::Nil: return "nil";
            case ValueKind::Bool: return num != 0 ? "true" : "false";
            case ValueKind::Num: return formatNumber(num);
            default: return str;
        }
    }
    bool equals(const Value& o) const {
        if (kind != o.kind) return false;
        switch (kind) {
            case ValueKind::Nil: return true;
            case ValueKind::Str: return str == o.str;
            default: return num == o.num;
        }
    }
    static Value parse(const std::string& s);
};

// ---------------------------------------------------------------- OpCode
enum class OpCode : uint8_t {
    Nop = 0, Const, Nil, True, False, Pop, GetLocal, SetLocal, GetGlobal, SetGlobal,
    Add, Sub, Mul, Div, Mod, Neg, Not, Eq, Ne, Lt, Le, Gt, Ge,
    Jump, JumpIfFalse, JumpIfFalseKeep, JumpIfTrueKeep, Call, CallNative, Return,
};

// ---------------------------------------------------------------- Chunk
struct FunctionInfo { std::string name; int arity = 0; int locals = 0; int entry = -1; };
struct HandlerInfo { std::string event; std::optional<std::string> target; int function = 0; };

struct Chunk {
    static constexpr const char* Magic = "MGBC";
    static constexpr int Version = 1;

    std::string name = "script";
    std::vector<uint8_t> code;
    std::vector<Value> constants;
    std::vector<FunctionInfo> functions;
    std::vector<HandlerInfo> handlers;
    std::vector<std::string> natives;
    int initFunction = -1;

    std::vector<uint8_t> write() const;
    static std::shared_ptr<Chunk> read(const std::vector<uint8_t>& data);
    std::string disassemble() const;
};

// ---------------------------------------------------------------- Lexer
enum class Tok {
    Eof, Number, String, Ident,
    LParen, RParen, LBrace, RBrace, Comma, Semi,
    Plus, Minus, Star, Slash, Percent, Bang,
    Assign, PlusEq, MinusEq, Eq, Ne, Lt, Le, Gt, Ge, AndAnd, OrOr,
};
struct Token { Tok kind; std::string text; double num; int line; };
std::vector<Token> tokenize(const std::string& src);

// Compiler (Compiler.cpp)
std::shared_ptr<Chunk> compileScript(const std::string& source, const std::string& name);

// ---------------------------------------------------------------- VM
class ScriptHost;

struct ScriptTask;
struct ScriptContext {
    World* world = nullptr;
    ScriptHost* host = nullptr;
    ScriptTask* task = nullptr;
    Entity* self = nullptr;
    bool isPlayer = false;
};

using NativeFn = std::function<Value(ScriptContext&, std::vector<Value>&)>;
struct NativeInfo { std::string name; int arity = 0; NativeFn fn; };

struct ScriptTask {
    int waitTicks = 0;
    bool waitDialog = false;
    bool done() const { return done_; }
    std::string name;

    ScriptTask(std::shared_ptr<Chunk> chunk, int function, const std::vector<Value>* args, std::string nm);
    void run(ScriptContext& ctx, std::map<std::string, Value>& globals);

private:
    struct Frame { int func = 0, ip = 0, base = 0; };
    std::shared_ptr<Chunk> c_;
    std::vector<Value> stack_ = std::vector<Value>(512);
    int sp_ = 0;
    Frame frames_[64];
    int fp_ = 0;
    bool done_ = false;
    void push(int func, const std::vector<Value>& args);
};

class ScriptNatives {
public:
    static NativeInfo* find(const std::string& name);
    static void registerNative(const std::string& name, int arity, NativeFn fn);
private:
    static void init();
};

class ScriptHost {
public:
    ScriptHost(World* world, std::shared_ptr<Chunk> chunk, Entity* self = nullptr, bool isPlayer = false, bool runInit = true);
    bool hasHandler(const std::string& ev) const;
    void flush() { runTasks(); }
    std::map<std::string, Value> captureGlobals() const { return globals_; }
    void restoreGlobals(const std::map<std::string, Value>* g);
    void fire(const std::string& ev, const std::optional<std::string>& target = std::nullopt);
    void dialogClosed();
    void tick();
private:
    std::shared_ptr<Chunk> chunk_;
    World* world_;
    std::map<std::string, Value> globals_;
    std::vector<std::unique_ptr<ScriptTask>> tasks_;
    ScriptContext ctx_;
    void spawn(int func, const std::string& name);
    void runTasks();
};

// Loads scripts by id from baseDir: scripts/<id>.mgb (bytecode) wins over .mgs (source).
struct ScriptLibrary {
    static std::string& baseDir() { static std::string d; return d; }
    static std::shared_ptr<Chunk> get(const std::string& id);
    static void clearCache();
};

} // namespace mg::scripting
