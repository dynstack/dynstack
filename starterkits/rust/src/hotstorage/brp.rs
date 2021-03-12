use super::{
    hotstorage_model::{CraneMove, CraneSchedule, World},
    search::depth_first_search,
};
use std::{collections::HashMap, iter::once};

type StackId = i32;
type BlockId = i32;
type Priority = u32;

/// Generate a schedule for the current world state by solving an restricted offline BRP
pub fn calculate_schedule(world: &World) -> CraneSchedule {
    let priorities = prioritize_by_due_date(world);
    let initial_state = BrpState::new(world, priorities);
    let solution = depth_first_search(initial_state);
    create_schedule_from_solution(world, solution)
}

/// Assign a priority to each block based on its due date.
/// This is not a good strategy but it is very simple.
/// The block with the lowest priority (due date) has to be retrieved first.
fn prioritize_by_due_date(world: &World) -> HashMap<BlockId, Priority> {
    let mut all_blocks: Vec<_> = world
        .get_Production()
        .get_BottomToTop()
        .iter()
        .chain(
            world
                .get_Buffers()
                .iter()
                .flat_map(|stack| stack.get_BottomToTop()),
        )
        .collect();

    // simply prioritize by due date
    all_blocks.sort_by_key(|block| block.get_Due().get_MilliSeconds());
    all_blocks
        .iter()
        .map(|block| block.get_Id())
        .zip(0..)
        .collect()
}

/// Translates the BRP solution into a CraneSchedule
fn create_schedule_from_solution(world: &World, moves: Vec<Move>) -> CraneSchedule {
    let mut schedule = CraneSchedule::new();
    let handover = world.get_Handover();
    let is_ready = handover.get_Ready();
    for opt_mov in moves.into_iter().take(3) {
        if !is_ready && opt_mov.tgt() == handover.get_Id() {
            break;
        }
        let mut mov = CraneMove::new();
        mov.set_BlockId(opt_mov.block());
        mov.set_SourceId(opt_mov.src());
        mov.set_TargetId(opt_mov.tgt());
        schedule.mut_Moves().push(mov);
    }
    schedule
}

/// A block for the BRP
#[derive(Clone, Copy, Debug)]
struct Block {
    id: BlockId,
    prio: Priority,
}

/// A possible move for the BRP
#[derive(Clone, Copy, Debug)]
pub struct Move {
    src: i32,
    tgt: i32,
    block: BlockId,
}

impl Move {
    pub fn src(&self) -> StackId {
        self.src
    }
    pub fn tgt(&self) -> StackId {
        self.tgt
    }
    pub fn block(&self) -> BlockId {
        self.block
    }
}

/// A stack for the BlockRelocationProblem
#[derive(Clone, Debug)]
struct Stack {
    id: StackId,
    max_height: usize,
    blocks: Vec<Block>,
}

impl Stack {
    /// Get the Block on top of the stack
    fn top(&self) -> Option<Block> {
        self.blocks.last().copied()
    }

    /// Get the block that has to be removed before all others in the stack.
    /// This is currently O(n). Caching this was avoided to keep the example as simple as possible.
    fn most_urgent(&self) -> Option<Block> {
        self.blocks.iter().min_by_key(|block| block.prio).copied()
    }
}

/// The state information for simple constrained offline BlockRelocationProblem (BRP)
/// For each state we can get the valid moves in this state and we can apply a move to get a new state.
/// This can be used to implement a simple search algorithm.
#[derive(Clone, Debug)]
pub struct BrpState {
    stacks: Vec<Stack>,
    moves: Vec<Move>,
    arrival_id: StackId,
    handover_id: StackId,
}

impl BrpState {
    /// Construct a new BRP from a given world state and an mapping from blocks to priorities.
    pub fn new(world: &World, priorities: HashMap<BlockId, Priority>) -> Self {
        let prod = world.get_Production();

        let stacks = once(prod)
            .chain(world.get_Buffers())
            .map(|stack| {
                let blocks = stack
                    .get_BottomToTop()
                    .iter()
                    .map(|block| {
                        let id = block.get_Id();
                        let prio = *priorities.get(&id).unwrap_or(&10000);
                        Block { id, prio }
                    })
                    .collect();
                Stack {
                    id: stack.get_Id(),
                    max_height: stack.get_MaxHeight() as usize,
                    blocks,
                }
            })
            .collect();

        Self {
            stacks,
            moves: Vec::new(),
            arrival_id: world.get_Production().get_Id(),
            handover_id: world.get_Handover().get_Id(),
        }
    }

    pub fn is_solved(&self) -> bool {
        self.not_empty_stacks().next().is_none()
    }

    /// Consumes the state and returns the moves performed to get to it.
    pub fn into_moves(self) -> Vec<Move> {
        self.moves
    }

    fn not_full_stacks(&self) -> impl Iterator<Item = &Stack> {
        self.stacks.iter().filter(|s| s.blocks.len() < s.max_height)
    }
    fn not_empty_stacks(&self) -> impl Iterator<Item = &Stack> {
        self.stacks.iter().filter(|s| !s.blocks.is_empty())
    }

    /// Apply a move to this instance and return the new state.
    /// If the move is invalid this method panics.
    pub fn apply_move(&self, mov: Move) -> Self {
        let mut result = self.clone();
        let block = result.stacks[mov.src as usize].blocks.pop().unwrap();
        assert_eq!(block.id, mov.block);
        if mov.tgt != self.handover_id {
            let tgt = &mut result.stacks[mov.tgt as usize];
            tgt.blocks.push(block);
            assert!(tgt.blocks.len() <= tgt.max_height);
        }
        result.moves.push(mov);
        result
    }

    /// Fills the moves parameter with all forced moves that can be applied to this state.
    /// Forced moves are those that either remove the next block or relocate blocks that prevent the removal of the next block.
    pub fn forced_moves(&self, moves: &mut Vec<Move>) {
        moves.clear();
        if let Some(src) = self
            .not_empty_stacks()
            .min_by_key(|stack| stack.most_urgent().unwrap().prio)
        {
            let urgent = src.most_urgent().unwrap();
            let top = src.top().unwrap();
            if urgent.id == top.id {
                moves.push(Move {
                    src: src.id,
                    tgt: self.handover_id,
                    block: top.id,
                });
            } else {
                for tgt in self.not_full_stacks() {
                    if src.id == tgt.id {
                        continue;
                    }
                    moves.push(Move {
                        src: src.id,
                        tgt: tgt.id,
                        block: top.id,
                    });
                }
            }
        }
    }
}
