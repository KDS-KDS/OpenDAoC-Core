using DOL.GS.PlayerClass;

namespace DOL.GS.Scripts
{
    //public class MimicWarrior : MimicNPC
    //{
    //    public MimicWarrior(byte level) : base(new ClassWarrior(), level)
    //    { }
    //}

    public class WarriorSpec : MimicSpec
    {
        public WarriorSpec()
        {
            SpecName = "WarriorSpec";

            int randBaseWeap = Util.Random(2);
            
            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.Sword; break;
                case 1: WeaponOneType = eObjectType.Axe; break;
                case 2: WeaponOneType = eObjectType.Hammer; break;
            }

            int randVariance = Util.Random(1);

            SpecType = eSpecType.Mid;

            switch (randVariance)
            {
                case 0:
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 39, 0.2f);
                Add(Specs.Thrown_Weapons, 13, 0.0f);
                break;

                case 1:
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Shields, 50, 0.5f);
                Add(Specs.Parry, 28, 0.2f);
                Add(Specs.Thrown_Weapons, 13, 0.0f);
                break;
            }
        }
    }
}