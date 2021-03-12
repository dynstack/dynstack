from rollingmill.rollingmill_model_pb2 import World
from rollingmill import heuristic

def plan_moves(world_data):
    print("plan")
    world = World()
    world.ParseFromString(world_data)
    plan = heuristic.next_moves(world)
    if plan:
        plan.SequenceNr = world.CraneMoves.SequenceNr + 1
    return plan