namespace MazeGame.Core.Scripting;

/// <summary>
/// Bytecode instructions of the script VM (a stack machine). Operand sizes: u8 = 1 byte, u16 / i16 = 2 bytes little endian.
/// Jump offsets are relative to the end of the instruction, so function bodies can be moved around freely.
/// </summary>
public enum OpCode : byte
{
    Nop = 0,
    Const,          // u16 constant index          push constants[i]
    Nil,            //                              push nil
    True,           //                              push true
    False,          //                              push false
    Pop,            //                              discard top
    GetLocal,       // u8 slot                      push locals[slot]
    SetLocal,       // u8 slot                      locals[slot] = pop
    GetGlobal,      // u16 constant (name)          push globals[name]
    SetGlobal,      // u16 constant (name)          globals[name] = pop
    Add, Sub, Mul, Div, Mod,                       // binary arithmetic (Add also joins strings)
    Neg, Not,                                      // unary
    Eq, Ne, Lt, Le, Gt, Ge,                        // comparisons
    Jump,           // i16 offset
    JumpIfFalse,    // i16 offset                   pops the condition
    JumpIfFalseKeep,// i16 offset                   keeps the value when jumping (for &&), pops otherwise
    JumpIfTrueKeep, // i16 offset                   keeps the value when jumping (for ||), pops otherwise
    Call,           // u16 function, u8 argc        call a script function
    CallNative,     // u16 native, u8 argc          call a host function (may block the task: wait, say...)
    Return,         //                              return the top of the stack (nil when the function ends)
}
