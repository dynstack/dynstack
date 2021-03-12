from hotstorage.hotstorage_model_pb2 import World
from hotstorage import heuristic, search

def plan_moves(world_data, use_heuristic):
    world = World()
    world.ParseFromString(world_data)
    if use_heuristic:
        crane_schedule = heuristic.crane_schedule(world)
    else:
        crane_schedule = search.crane_schedule(world)
    print(world, use_heuristic, crane_schedule)
    if crane_schedule:
        crane_schedule.SequenceNr = world.Crane.Schedule.SequenceNr + 1
    return crane_schedule